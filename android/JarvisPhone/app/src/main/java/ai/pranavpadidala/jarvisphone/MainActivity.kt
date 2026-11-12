package ai.pranavpadidala.jarvisphone

import android.app.Activity
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

class MainActivity : Activity() {
    private lateinit var addressInput: EditText
    private lateinit var portInput: EditText
    private lateinit var tokenInput: EditText
    private lateinit var commandInput: EditText
    private lateinit var statusView: TextView
    private lateinit var queueContainer: LinearLayout
    private lateinit var executor: ExecutorService
    private lateinit var configStore: BridgeConfigStore
    private val refreshHandler = Handler(Looper.getMainLooper())
    private val autoRefresh = object : Runnable {
        override fun run() {
            if (::tokenInput.isInitialized && tokenInput.text.isNotEmpty()) refreshQueue()
            refreshHandler.postDelayed(this, 30_000L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        executor = Executors.newSingleThreadExecutor()
        configStore = BridgeConfigStore(this)
        buildScreen()
        restoreSavedConfig()
    }

    private fun buildScreen() {
        val page = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(18), dp(18), dp(18))
        }
        val scroll = ScrollView(this)
        val content = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(content)

        content.addView(text("JARVIS Phone", 25f, true))
        content.addView(text("Private command inbox", 15f, false))
        content.addView(text("Connect both devices to the same Tailscale network. Enter the laptop's Tailscale IPv4 address and bridge token.", 14f, false))

        addressInput = edit("Laptop Tailscale IPv4 (100.x.x.x)")
        portInput = edit("Port", "47821").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        tokenInput = edit("Bridge bearer token").apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        commandInput = edit("Type a request for JARVIS").apply {
            minLines = 3
            gravity = android.view.Gravity.TOP
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
        }

        content.addView(label("Laptop address"))
        content.addView(addressInput)
        content.addView(label("Port"))
        content.addView(portInput)
        content.addView(label("Bridge token (encrypted on this device)"))
        content.addView(tokenInput)

        val connectButton = Button(this).apply { text = "Check connection" }
        connectButton.setOnClickListener { checkConnection() }
        content.addView(connectButton)

        content.addView(label("New request"))
        content.addView(commandInput)
        val sendButton = Button(this).apply { text = "Send to JARVIS queue" }
        sendButton.setOnClickListener { sendCommand() }
        content.addView(sendButton)

        val refreshButton = Button(this).apply { text = "Refresh queue" }
        refreshButton.setOnClickListener { refreshQueue() }
        content.addView(refreshButton)

        val forgetButton = Button(this).apply { text = "Forget saved connection" }
        forgetButton.setOnClickListener { forgetConnection() }
        content.addView(forgetButton)

        statusView = text("Not connected. The bridge must be enabled on the laptop.", 14f, false)
        content.addView(statusView)
        content.addView(text("Command history (auto-refreshes while this screen is open)", 19f, true))
        queueContainer = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        content.addView(queueContainer)

        page.addView(scroll, LinearLayout.LayoutParams(-1, 0, 1f))
        setContentView(page)
    }

    private fun checkConnection() {
        withConnection { host, port, token ->
            request(host, port, token, "GET", "/api/health")
            "Connected to JARVIS bridge."
        }
    }

    private fun sendCommand() {
        val command = commandInput.text.toString().trim()
        if (command.isEmpty()) {
            showStatus("Enter a request first.")
            return
        }
        if (command.length > 2000) {
            showStatus("Request is too long (maximum 2,000 characters).")
            return
        }
        withConnection { host, port, token ->
            val body = JSONObject()
                .put("requestId", UUID.randomUUID().toString())
                .put("text", command)
            request(host, port, token, "POST", "/api/commands", body)
            runOnUiThread { commandInput.setText("") }
            "Request added to the laptop queue. It still needs approval at the laptop."
        }
    }

    private fun refreshQueue() {
        withConnection { host, port, token ->
            val response = request(host, port, token, "GET", "/api/commands")
            val items = JSONArray(response)
            runOnUiThread {
                queueContainer.removeAllViews()
                var pending = 0
                var displayed = 0
                for (index in 0 until items.length()) {
                    val item = items.getJSONObject(index)
                    val state = item.optString("status", "Unknown")
                    if (state.equals("Queued", ignoreCase = true)) pending++
                    displayed++
                    val row = LinearLayout(this).apply {
                        orientation = LinearLayout.VERTICAL
                        setPadding(dp(8), dp(8), dp(8), dp(8))
                    }
                    row.addView(text(item.optString("text"), 15f, false))
                    row.addView(text("Status: $state", 13f, true))
                    row.addView(text(item.optString("createdAtUtc"), 11f, false))
                    if (state.equals("Queued", ignoreCase = true)) {
                        val cancel = Button(this).apply { text = "Cancel request" }
                        cancel.setOnClickListener { cancelCommand(item.optString("id")) }
                        row.addView(cancel)
                    }
                    queueContainer.addView(row)
                }
                if (displayed == 0) queueContainer.addView(text("No commands yet. Send a request to get started.", 14f, false))
                statusView.text = "Queue refreshed. Pending: $pending · History: $displayed"
            }
            "Queue refreshed."
        }
    }

    private fun cancelCommand(id: String) {
        withConnection { host, port, token ->
            request(host, port, token, "POST", "/api/commands/$id/cancel", JSONObject())
            "Request cancelled (if it had not already been approved)."
        }
    }

    private fun withConnection(action: (String, Int, String) -> String) {
        val host = addressInput.text.toString().trim()
        val port = portInput.text.toString().trim().toIntOrNull()
        val token = tokenInput.text.toString()
        if (!isTailscaleIpv4(host)) {
            showStatus("Use the laptop's Tailscale IPv4 address (100.64.0.0 to 100.127.255.255).")
            return
        }
        if (port == null || port !in 1024..65535) {
            showStatus("Enter a valid port from 1024 to 65535.")
            return
        }
        if (token.toByteArray(Charsets.UTF_8).size < 32) {
            showStatus("The bridge token must be at least 32 bytes.")
            return
        }

        showStatus("Connecting…")
        executor.execute {
            try {
                val message = action(host, port, token)
                configStore.save(BridgeConfigStore.Config(host, port, token))
                showStatus(message)
            } catch (ex: Exception) {
                showStatus("Connection failed: " + (ex.message ?: ex.javaClass.simpleName))
            }
        }
    }

    @Throws(IOException::class)
    private fun request(
        host: String,
        port: Int,
        token: String,
        method: String,
        path: String,
        body: JSONObject? = null
    ): String {
        val connection = (URL("http://$host:$port$path").openConnection() as HttpURLConnection)
        try {
            connection.requestMethod = method
            connection.connectTimeout = 7000
            connection.readTimeout = 10000
            connection.setRequestProperty("Authorization", "Bearer $token")
            connection.setRequestProperty("Accept", "application/json")
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                val bytes = body.toString().toByteArray(Charsets.UTF_8)
                connection.setFixedLengthStreamingMode(bytes.size)
                connection.outputStream.use { it.write(bytes) }
            }
            val status = connection.responseCode
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val response = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            if (status !in 200..299) {
                val message = try { JSONObject(response).optString("error", "HTTP $status") } catch (_: Exception) { "HTTP $status" }
                throw IOException(message)
            }
            return response
        } finally {
            connection.disconnect()
        }
    }

    private fun isTailscaleIpv4(value: String): Boolean {
        val parts = value.split(".")
        if (parts.size != 4) return false
        val numbers = parts.map { it.toIntOrNull() ?: return false }
        if (numbers.any { it !in 0..255 }) return false
        return numbers[0] == 100 && numbers[1] in 64..127
    }

    private fun restoreSavedConfig() {
        try {
            val saved = configStore.load() ?: return
            addressInput.setText(saved.host)
            portInput.setText(saved.port.toString())
            tokenInput.setText(saved.token)
            statusView.text = "Saved encrypted connection restored. Check connection to verify the laptop is reachable."
        } catch (ex: Exception) {
            configStore.clear()
            statusView.text = "Saved connection could not be decrypted and was cleared. Please pair again."
        }
    }

    private fun forgetConnection() {
        configStore.clear()
        addressInput.setText("")
        portInput.setText("47821")
        tokenInput.setText("")
        statusView.text = "Saved connection forgotten from this device."
    }

    private fun showStatus(message: String) {
        runOnUiThread { statusView.text = message }
    }

    private fun edit(hintText: String, initial: String = "") = EditText(this).apply {
        hint = hintText
        setText(initial)
        textSize = 15f
        setSingleLine(hintText != "Type a request for JARVIS")
    }

    private fun label(value: String) = text(value, 13f, true)

    private fun text(value: String, size: Float, bold: Boolean) = TextView(this).apply {
        text = value
        textSize = size
        if (bold) setTypeface(typeface, Typeface.BOLD)
        setPadding(0, dp(7), 0, dp(7))
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    override fun onResume() {
        super.onResume()
        refreshHandler.removeCallbacks(autoRefresh)
        if (::tokenInput.isInitialized && tokenInput.text.isNotEmpty()) {
            refreshQueue()
            refreshHandler.postDelayed(autoRefresh, 30_000L)
        }
    }

    override fun onPause() {
        refreshHandler.removeCallbacks(autoRefresh)
        super.onPause()
    }

    override fun onDestroy() {
        refreshHandler.removeCallbacks(autoRefresh)
        if (::executor.isInitialized) executor.shutdownNow()
        super.onDestroy()
    }
}
