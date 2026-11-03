package com.smartwarehouse.app

import android.annotation.SuppressLint
import android.app.DownloadManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.VibrationEffect
import android.os.Vibrator
import android.webkit.CookieManager
import android.webkit.JavascriptInterface
import android.webkit.URLUtil
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import org.json.JSONObject

/**
 * Оболочка веб-приложения склада: открывает сайт на сервере предприятия и добавляет то, чего нет в браузере
 * по http — сканирование камерой и встроенный сканер ТСД. Всё остальное (экраны, логика) — на сервере,
 * поэтому обновлять приложение на телефонах нужно редко: новые функции приходят вместе с сервером.
 *
 * Отсканированный код передаётся странице через window.swOnNativeScan(code) — так же, как код ручного
 * сканера: в документе добавляется строка, на остальных страницах открывается ячейка или позиция.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var web: WebView
    private lateinit var refresh: SwipeRefreshLayout
    private var serverUrl: String? = null
    private var scanReceiver: BroadcastReceiver? = null
    private var showingError = false
    private var askedForServer = false

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        web = findViewById(R.id.web)
        refresh = findViewById(R.id.refresh)

        with(web.settings) {
            javaScriptEnabled = true
            domStorageEnabled = true
            // По этой метке сайт понимает, что открыт в приложении (кнопка камеры и т.п.).
            userAgentString = "$userAgentString SmartWarehouseApp/${BuildConfig.VERSION_NAME}"
        }
        CookieManager.getInstance().setAcceptCookie(true)
        web.addJavascriptInterface(Bridge(), "SmartWarehouseApp")
        web.webViewClient = Client()
        web.setDownloadListener { url, userAgent, disposition, mime, _ -> download(url, userAgent, disposition, mime) }

        // Потянуть вниз — обновить страницу (только когда страница прокручена в самый верх).
        refresh.setOnChildScrollUpCallback { _, _ -> web.scrollY > 0 }
        refresh.setOnRefreshListener { reload() }

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (!showingError && web.canGoBack()) web.goBack() else finish()
            }
        })

        if (savedInstanceState != null) web.restoreState(savedInstanceState)
    }

    override fun onResume() {
        super.onResume()
        val url = Prefs.serverUrl(this)
        if (url == null) {
            // Вернулись из настроек, так и не указав адрес, — выходим, а не открываем настройки по кругу.
            if (askedForServer) finish()
            else startActivity(Intent(this, SettingsActivity::class.java).putExtra(SettingsActivity.FIRST_RUN, true))
            askedForServer = true
            return
        }
        if (url != serverUrl) {          // первый запуск или адрес сменили в настройках
            serverUrl = url
            web.loadUrl(url)
        }
        registerScanner()
    }

    override fun onPause() {
        unregisterScanner()
        CookieManager.getInstance().flush()   // вход сохраняется между запусками
        super.onPause()
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        web.saveState(outState)
    }

    // ---------- Сканирование ----------

    private fun scanWithCamera() {
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_ALL_FORMATS)
            .enableAutoZoom()
            .build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { barcode -> barcode.rawValue?.let(::deliver) }
            .addOnFailureListener { e ->
                toast("Камера недоступна: ${e.message ?: e.javaClass.simpleName}. Нужны сервисы Google Play.")
            }
    }

    /** Код — странице (тот же путь, что у ручного сканера). */
    private fun deliver(code: String) {
        val clean = code.trim()
        if (clean.isEmpty()) return
        vibrate()
        web.evaluateJavascript("window.swOnNativeScan && window.swOnNativeScan(${JSONObject.quote(clean)})", null)
    }

    /** Встроенный сканер ТСД, который присылает код сообщением (Intent), а не «печатает» его. */
    private fun registerScanner() {
        unregisterScanner()
        val (action, extra) = Prefs.scannerIntent(this) ?: return
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                val code = intent.getStringExtra(extra)
                    ?: intent.getByteArrayExtra(extra)?.toString(Charsets.UTF_8)
                if (code != null) deliver(code)
            }
        }
        // Сообщение приходит от системной службы сканера — получатель должен быть доступен извне.
        ContextCompat.registerReceiver(this, receiver, IntentFilter(action), ContextCompat.RECEIVER_EXPORTED)
        scanReceiver = receiver
    }

    private fun unregisterScanner() {
        scanReceiver?.let { runCatching { unregisterReceiver(it) } }
        scanReceiver = null
    }

    // ---------- Страница ----------

    private fun reload() {
        val url = serverUrl ?: return
        if (showingError) web.loadUrl(url) else web.reload()
    }

    private inner class Client : WebViewClient() {
        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            val server = serverUrl?.let(Uri::parse) ?: return false
            val target = request.url
            // Свой сервер — внутри приложения; чужие ссылки — в браузере телефона.
            if (target.host == server.host && target.port == server.port) return false
            runCatching { startActivity(Intent(Intent.ACTION_VIEW, target)) }
            return true
        }

        override fun onPageFinished(view: WebView, url: String) {
            refresh.isRefreshing = false
        }

        override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
            if (!request.isForMainFrame) return
            refresh.isRefreshing = false
            showError(error.description?.toString() ?: "нет связи")
        }
    }

    /** Сервер недоступен — понятная страница с кнопками «Повторить» и «Настройки». */
    private fun showError(reason: String) {
        showingError = true
        val html = """
            <html><head><meta name="viewport" content="width=device-width, initial-scale=1">
            <style>body{font-family:sans-serif;padding:24px;color:#1d2433}button{font-size:16px;padding:10px 18px;margin:6px 8px 0 0;border-radius:8px;border:1px solid #1F5FAD;background:#1F5FAD;color:#fff}
            button.alt{background:#fff;color:#1F5FAD}.muted{color:#6b7280}</style></head><body>
            <h2>Сервер склада недоступен</h2>
            <p>Адрес: <b>${serverUrl ?: "не задан"}</b></p>
            <p class="muted">${reason}. Проверьте, что телефон подключён к сети предприятия (Wi-Fi) и сервер включён.</p>
            <button onclick="SmartWarehouseApp.retry()">Повторить</button>
            <button class="alt" onclick="SmartWarehouseApp.openSettings()">Настройки</button>
            </body></html>
        """.trimIndent()
        web.loadDataWithBaseURL(null, html, "text/html", "utf-8", null)
    }

    /** PDF (акты, наклейки) — в «Загрузки» с тем же входом (cookie), открыть — из уведомления. */
    private fun download(url: String, userAgent: String, disposition: String?, mime: String?) {
        try {
            val fileName = URLUtil.guessFileName(url, disposition, mime)
            val request = DownloadManager.Request(Uri.parse(url))
                .addRequestHeader("Cookie", CookieManager.getInstance().getCookie(url) ?: "")
                .addRequestHeader("User-Agent", userAgent)
                .setMimeType(mime)
                .setTitle(fileName)
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, fileName)
            (getSystemService(DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
            toast("Скачивается: $fileName — откройте из уведомления")
        } catch (e: Exception) {
            toast("Не удалось скачать: ${e.message}")
        }
    }

    private fun vibrate() {
        val vibrator = getSystemService(VIBRATOR_SERVICE) as? Vibrator ?: return
        if (Build.VERSION.SDK_INT >= 26) vibrator.vibrate(VibrationEffect.createOneShot(60, VibrationEffect.DEFAULT_AMPLITUDE))
        else @Suppress("DEPRECATION") vibrator.vibrate(60)
    }

    private fun toast(text: String) = Toast.makeText(this, text, Toast.LENGTH_LONG).show()

    /** Что страница может вызвать у приложения: window.SmartWarehouseApp.scanCamera() и т.д. */
    private inner class Bridge {
        @JavascriptInterface
        fun scanCamera() = runOnUiThread { scanWithCamera() }

        @JavascriptInterface
        fun openSettings() = runOnUiThread { startActivity(Intent(this@MainActivity, SettingsActivity::class.java)) }

        @JavascriptInterface
        fun retry() = runOnUiThread {
            showingError = false
            serverUrl?.let(web::loadUrl)
        }

        @JavascriptInterface
        fun version(): String = BuildConfig.VERSION_NAME

        /** Сканер ТСД настроен на сообщения (Intent) — страница может подсказать «нажмите кнопку сканера». */
        @JavascriptInterface
        fun hasHardwareScanner(): Boolean = Prefs.scannerIntent(this@MainActivity) != null
    }
}
