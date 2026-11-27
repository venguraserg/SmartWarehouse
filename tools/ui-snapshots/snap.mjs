// Снимки главных страниц на ширине компьютера и телефона — для проверки вёрстки в PR.
// Запуск: node snap.mjs <адрес веб-интерфейса> <seed.json> <папка для снимков>
import { chromium, devices } from "playwright";
import fs from "node:fs";
import path from "node:path";

const [base = "http://localhost:5200", seedFile = "seed.json", out = "shots"] = process.argv.slice(2);
const ids = JSON.parse(fs.readFileSync(seedFile, "utf8"));
fs.mkdirSync(out, { recursive: true });

const inv = ids.inventory;
const pages = [
  ["01-home", "/"],
  ["02-stock-places", "/stock"],
  ["03-stock-items", "/stock?view=items"],
  ["04-items", "/items"],
  ["05-stock-search-note", "/stock?view=items&search=" + encodeURIComponent("подшипник гомогенизатор")],
  ["06-item-card", `/items/${ids.b6205}`],
  ["07-custody", "/custody"],
  ["08-warehouses", "/warehouses"],
  ["09-labels", "/labels"],
  ["10-inventory-list", "/inventory"],
  ["11-inventory-places", `/inventory/${inv}`],
  ["12-inventory-count", `/inventory/${inv}/place/${ids.cell2}`],
  ["13-inventory-sheet", `/inventory/${inv}/sheet`],
  ["14-location-qr", "/l/SK1-1-C3-1-1"],
  ["15-admin-menu", "/admin/menu"],
  ["16-documents", "/documents"],
];

// Главная под сотрудником (вход mech) — вторая роль.
const employeePages = [["17-home-employee", "/"]];

const problems = [];

async function shoot(name, viewport, login = "demo", list = pages) {
  const browser = await chromium.launch();
  const context = await browser.newContext(viewport);
  const page = await context.newPage();
  page.on("pageerror", e => problems.push(`${name}: ошибка JS: ${e.message}`));

  await page.goto(base + "/login");
  await page.fill("#login", login);
  await page.fill("#password", "demo12345");
  await Promise.all([page.waitForURL(u => !u.pathname.startsWith("/login")), page.click("button[type=submit]")]);

  for (const [file, url] of list) {
    const response = await page.goto(base + url, { waitUntil: "networkidle" });
    await page.waitForTimeout(700); // Blazor дорисовывает после подключения
    if (!response || response.status() >= 400) problems.push(`${name} ${url}: код ${response?.status()}`);
    const text = await page.locator("body").innerText();
    if (/An unhandled error|Произошла ошибка|Не удалось загрузить/.test(text)) problems.push(`${name} ${url}: сообщение об ошибке на странице`);
    // Горизонтальная прокрутка страницы на телефоне — признак сломанной вёрстки.
    if (name === "phone") {
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      if (overflow > 2) problems.push(`phone ${url}: страница шире экрана на ${overflow}px`);
    }
    await page.screenshot({ path: path.join(out, `${name}-${file}.png`), fullPage: true });
  }
  await browser.close();
}

await shoot("desktop", { viewport: { width: 1366, height: 900 } });
await shoot("phone", { ...devices["iPhone 12"] });
await shoot("desktop", { viewport: { width: 1366, height: 900 } }, "mech", employeePages);
await shoot("phone", { ...devices["iPhone 12"] }, "mech", employeePages);

fs.writeFileSync(path.join(out, "problems.txt"), problems.join("\n") || "Замечаний нет.");
console.log(problems.length ? "Замечания:\n" + problems.join("\n") : "Замечаний нет.");
