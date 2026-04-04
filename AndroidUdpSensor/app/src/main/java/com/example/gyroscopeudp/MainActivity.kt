package com.example.gyroscopeudp

import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean

class MainActivity : AppCompatActivity(), SensorEventListener {

    // ---- Sensor ----
    private lateinit var sensorManager: SensorManager
    private var rotationSensor: Sensor? = null
    private var accelerometerSensor: Sensor? = null

    // ---- 加速度暫存（sensorHandler thread 寫，同 thread 讀，無競爭）----
    private var accX = 0f
    private var accY = 0f
    private var accZ = 0f

    // ---- UDP ----
    private var udpSocket: DatagramSocket? = null
    private var targetAddress: InetAddress? = null
    private var targetPort = 9999

    // ---- 狀態 ----
    private val isRunning = AtomicBoolean(false)
    private var sensorThread: HandlerThread? = null
    private var sensorHandler: Handler? = null

    // ---- Hz 計算 ----
    private var packetCount = 0L
    private var hzStartTime = System.currentTimeMillis()
    private val uiHandler = Handler(Looper.getMainLooper())

    // ---- UI ----
    private lateinit var tvStatus: TextView
    private lateinit var tvHz: TextView
    private lateinit var etIp: EditText
    private lateinit var etPort: EditText
    private lateinit var btnToggle: Button

    // -------------------------------------------------------
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        tvStatus  = findViewById(R.id.tvStatus)
        tvHz      = findViewById(R.id.tvHz)
        etIp      = findViewById(R.id.etIp)
        etPort    = findViewById(R.id.etPort)
        btnToggle = findViewById(R.id.btnToggle)

        sensorManager = getSystemService(SENSOR_SERVICE) as SensorManager
        rotationSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
        accelerometerSensor = sensorManager.getDefaultSensor(Sensor.TYPE_LINEAR_ACCELERATION)

        if (rotationSensor == null) {
            tvStatus.text = "❌ 裝置不支援陀螺儀"
            btnToggle.isEnabled = false
        }

        btnToggle.setOnClickListener {
            if (isRunning.get()) stopSending() else startSending()
        }
    }

    // -------------------------------------------------------
    private fun startSending() {
        val ip = etIp.text.toString().trim()
        targetPort = etPort.text.toString().toIntOrNull() ?: 9999

        if (ip.isEmpty()) {
            tvStatus.text = "⚠️ 請輸入 Unity 電腦的 IP"
            return
        }

        Thread {
            try {
                targetAddress = InetAddress.getByName(ip)
                udpSocket = DatagramSocket()
                isRunning.set(true)

                sensorThread = HandlerThread("GyroSensor").also { it.start() }
                sensorHandler = Handler(sensorThread!!.looper)

                sensorManager.registerListener(
                    this@MainActivity,
                    rotationSensor,
                    SensorManager.SENSOR_DELAY_FASTEST,
                    sensorHandler
                )

                // 加速度計用 GAME 速率即可（不需要最快）
                accelerometerSensor?.let {
                    sensorManager.registerListener(
                        this@MainActivity,
                        it,
                        SensorManager.SENSOR_DELAY_GAME,
                        sensorHandler
                    )
                }

                uiHandler.post {
                    tvStatus.text = "✅ 傳送中 → $ip:$targetPort"
                    btnToggle.text = "停止"
                }
            } catch (e: Exception) {
                uiHandler.post { tvStatus.text = "❌ ${e.message}" }
            }
        }.start()
    }

    private fun stopSending() {
        isRunning.set(false)
        sensorManager.unregisterListener(this)
        sensorThread?.quitSafely()
        sensorThread = null
        udpSocket?.close()
        udpSocket = null

        tvStatus.text = "已停止"
        btnToggle.text = "開始傳送"
        tvHz.text = "0 Hz"
        packetCount = 0
    }

    // -------------------------------------------------------
    // SensorEventListener — 在 sensorHandler thread 執行
    // -------------------------------------------------------
    override fun onSensorChanged(event: SensorEvent) {
        when (event.sensor.type) {

            // 加速度計：只更新暫存值，不發送封包
            Sensor.TYPE_LINEAR_ACCELERATION -> {
                accX = event.values[0]
                accY = event.values[1]
                accZ = event.values[2]
            }

            // 旋轉向量：打包四元數 + 最新加速度，一起發送
            Sensor.TYPE_GAME_ROTATION_VECTOR -> {
                if (!isRunning.get()) return
                val v = event.values

                val qx = v[0]
                val qy = v[1]
                val qz = v[2]
                val qw = if (v.size >= 4) v[3] else
                    Math.sqrt((1.0 - qx * qx - qy * qy - qz * qz).coerceAtLeast(0.0)).toFloat()

                // 封包格式 28 bytes Big-Endian：
                //   [0-15]  四元數 qx, qy, qz, qw  (各 4 bytes float)
                //   [16-27] 線性加速度 ax, ay, az   (各 4 bytes float, m/s²)
                val buf = ByteBuffer.allocate(28).order(ByteOrder.BIG_ENDIAN)
                buf.putFloat(qx).putFloat(qy).putFloat(qz).putFloat(qw)
                buf.putFloat(accX).putFloat(accY).putFloat(accZ)
                val bytes = buf.array()

                try {
                    udpSocket?.send(DatagramPacket(bytes, 28, targetAddress, targetPort))
                } catch (_: Exception) { /* 忽略單次發送失敗 */ }

                // 每秒更新一次 Hz 顯示
                packetCount++
                val now = System.currentTimeMillis()
                val elapsed = now - hzStartTime
                if (elapsed >= 1000L) {
                    val hz = packetCount * 1000L / elapsed
                    packetCount = 0
                    hzStartTime = now
                    uiHandler.post { tvHz.text = "$hz Hz" }
                }
            }
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}

    // -------------------------------------------------------
    override fun onDestroy() {
        stopSending()
        super.onDestroy()
    }
}
