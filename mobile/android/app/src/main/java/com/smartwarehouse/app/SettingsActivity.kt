package com.smartwarehouse.app

import android.os.Bundle
import android.text.InputType
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

/** Адрес сервера склада и способ работы встроенного сканера ТСД. Экран собран в коде — без лишних файлов разметки. */
class SettingsActivity : AppCompatActivity() {

    companion object {
        const val FIRST_RUN = "first_run"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val pad = (16 * resources.displayMetrics.density).toInt()

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
        }

        fun label(text: String, top: Boolean = true) = TextView(this).apply {
            this.text = text
            textSize = 15f
            if (top) setPadding(0, pad, 0, pad / 4)
        }.also(root::addView)

        fun hint(text: String) = TextView(this).apply {
            this.text = text
            textSize = 13f
            setTextColor(0xFF6B7280.toInt())
        }.also(root::addView)

        if (intent.getBooleanExtra(FIRST_RUN, false)) {
            TextView(this).apply {
                text = "Добро пожаловать! Укажите адрес сервера склада — тот, по которому сайт открывается в браузере."
                textSize = 16f
            }.also(root::addView)
        }

        label("Адрес сервера")
        val server = EditText(this).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
            hint = "http://192.168.1.10:8081"
            setText(Prefs.serverUrl(this@SettingsActivity) ?: "")
        }.also(root::addView)
        hint("Узнайте у администратора склада. Можно без «http://»: 192.168.1.10:8081")

        label("Встроенный сканер ТСД")
        val modes = ScannerMode.entries
        val mode = Spinner(this).apply {
            adapter = ArrayAdapter(this@SettingsActivity, android.R.layout.simple_spinner_dropdown_item, modes.map { it.title })
            setSelection(modes.indexOf(Prefs.scannerMode(this@SettingsActivity)))
        }.also(root::addView)
        val modeHint = hint("")

        val custom = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val action = EditText(this).apply { hint = "Действие (action), например android.intent.action.SCANRESULT"; setText(Prefs.customAction(this@SettingsActivity)) }
        val extra = EditText(this).apply { hint = "Поле с кодом (extra), например value"; setText(Prefs.customExtra(this@SettingsActivity)) }
        custom.addView(action)
        custom.addView(extra)
        root.addView(custom)

        fun updateMode() {
            val selected = modes[mode.selectedItemPosition]
            custom.visibility = if (selected == ScannerMode.CUSTOM) View.VISIBLE else View.GONE
            modeHint.text = when (selected) {
                ScannerMode.KEYBOARD -> "Сканер «печатает» код как клавиатура (ввод с клавиатуры, Enter в конце). Подходит большинству ТСД и Bluetooth-сканеров, настраивать ничего не нужно."
                ScannerMode.ZEBRA -> "В DataWedge создайте профиль для приложения «Склад»: Intent output → включить, действие ${selected.action}, доставка Broadcast. Keystroke output — выключить."
                ScannerMode.CUSTOM -> "Действие и поле с кодом — из настроек сканера ТСД (раздел «Вывод данных» / «Intent / Broadcast»)."
                else -> "В настройках сканера ТСД включите вывод по сообщению (Broadcast / Intent) и выключите ввод с клавиатуры. Действие: ${selected.action}, поле: ${selected.extra}."
            }
        }
        mode.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) = updateMode()
            override fun onNothingSelected(parent: AdapterView<*>?) = Unit
        }
        updateMode()

        val error = TextView(this).apply { setTextColor(0xFFC0262D.toInt()); setPadding(0, pad / 2, 0, 0) }
        root.addView(error)

        Button(this).apply {
            text = "Сохранить"
            setOnClickListener {
                val url = Prefs.normalizeServer(server.text.toString())
                if (url == null) {
                    error.text = "Укажите адрес сервера, например 192.168.1.10:8081"
                    return@setOnClickListener
                }
                Prefs.save(this@SettingsActivity, url, modes[mode.selectedItemPosition], action.text.toString(), extra.text.toString())
                finish()
            }
        }.also { root.addView(it, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = pad }) }

        hint("Версия приложения ${BuildConfig.VERSION_NAME}")

        setContentView(ScrollView(this).apply { addView(root) })
    }
}
