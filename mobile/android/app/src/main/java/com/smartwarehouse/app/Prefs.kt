package com.smartwarehouse.app

import android.content.Context

/** Сканер ТСД: как он передаёт код в приложение. */
enum class ScannerMode(val title: String, val action: String?, val extra: String?) {
    /** Сканер «печатает» как клавиатура — веб-приложение распознаёт сам, настраивать ничего не нужно. */
    KEYBOARD("Как клавиатура (по умолчанию)", null, null),

    /** Zebra DataWedge: профиль с выводом Intent (broadcast) на действие ниже. */
    ZEBRA("Zebra (DataWedge, Intent)", "com.smartwarehouse.SCAN", "com.symbol.datawedge.data_string"),

    /** Urovo / Chainway и другие со «стандартным» сообщением сканера. */
    UROVO("Urovo, Chainway (ACTION_DECODE_DATA)", "android.intent.ACTION_DECODE_DATA", "barcode_string"),

    /** Honeywell: Data Intent в настройках сканера. */
    HONEYWELL("Honeywell (Data Intent)", "com.honeywell.decode.intent.action.EDIT_DATA", "data"),

    /** Newland. */
    NEWLAND("Newland", "nlscan.action.SCANNER_RESULT", "SCAN_BARCODE1"),

    /** iData, Seuic и другие — действие и поле задаются вручную. */
    CUSTOM("Другой (указать вручную)", null, null);
}

/** Настройки приложения на телефоне: адрес сервера и сканер. */
object Prefs {
    private const val FILE = "settings"
    private const val SERVER = "server"
    private const val MODE = "scanner_mode"
    private const val ACTION = "scanner_action"
    private const val EXTRA = "scanner_extra"

    private fun prefs(context: Context) = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    fun serverUrl(context: Context): String? = prefs(context).getString(SERVER, null)

    fun scannerMode(context: Context): ScannerMode =
        runCatching { ScannerMode.valueOf(prefs(context).getString(MODE, null) ?: "") }.getOrDefault(ScannerMode.KEYBOARD)

    fun customAction(context: Context): String = prefs(context).getString(ACTION, "") ?: ""

    fun customExtra(context: Context): String = prefs(context).getString(EXTRA, "") ?: ""

    /** Действие и поле сообщения сканера ТСД; null — сканер работает как клавиатура. */
    fun scannerIntent(context: Context): Pair<String, String>? {
        val mode = scannerMode(context)
        val action = if (mode == ScannerMode.CUSTOM) customAction(context) else mode.action
        val extra = if (mode == ScannerMode.CUSTOM) customExtra(context) else mode.extra
        return if (action.isNullOrBlank() || extra.isNullOrBlank()) null else action.trim() to extra.trim()
    }

    fun save(context: Context, server: String, mode: ScannerMode, action: String, extra: String) {
        prefs(context).edit()
            .putString(SERVER, server)
            .putString(MODE, mode.name)
            .putString(ACTION, action)
            .putString(EXTRA, extra)
            .apply()
    }

    /** Адрес сервера: «192.168.1.10:8081» → «http://192.168.1.10:8081»; null — неверный. */
    fun normalizeServer(input: String): String? {
        var value = input.trim().trimEnd('/')
        if (value.isEmpty()) return null
        if (!value.startsWith("http://", true) && !value.startsWith("https://", true)) value = "http://$value"
        val uri = android.net.Uri.parse(value)
        return if (uri.host.isNullOrBlank()) null else value
    }
}
