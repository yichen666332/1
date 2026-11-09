/*
  Arduino 数传/USB 双通道发送示例（与 Qt SerialFrameParser 二进制帧一致）
  - 每帧 19 字节：0xAA + 4×float32 LE(raw, filt, tofRaw, tofFilt) + XOR(前 17 字节) + 0x55
  - raw/filt = 超声(cm)；tofRaw/tofFilt = 激光(cm)
  - USB to TTL：通过 Serial 输出（9600 等，与 Qt 所选波特率一致）
  - 数传：通过 Serial1 输出（Mega: TX1=18, RX1=19），115200 锁死

  Qt：USB to TTL / 数传模块 两种模式均把收到的字节交给同一解析器，无文本行协议。
*/

enum TxMode {
  TX_USB_TTL = 0,
  TX_DATA_RADIO = 1
};

TxMode g_txMode = TX_DATA_RADIO;

const unsigned long SEND_INTERVAL_MS = 100;
unsigned long g_lastSendMs = 0;

float g_demoLaser = 50.0f;
float g_demoUltra = 80.0f;
int g_dir = 1;

static void float32ToLeBytes(float v, uint8_t *out4) {
  union {
    float f;
    uint32_t u;
  } x;
  x.f = v;
  uint32_t w = x.u;
  out4[0] = (uint8_t)(w & 0xFFu);
  out4[1] = (uint8_t)((w >> 8) & 0xFFu);
  out4[2] = (uint8_t)((w >> 16) & 0xFFu);
  out4[3] = (uint8_t)((w >> 24) & 0xFFu);
}

static void writeBinarySensorFrame(Stream &port, float ultrasonic_cm, float laser_cm, bool laser_valid) {
  uint8_t buf[19];
  buf[0] = 0xAA;
  const float u = ultrasonic_cm;
  const float l = laser_valid ? laser_cm : 0.0f;
  float32ToLeBytes(u, &buf[1]);
  float32ToLeBytes(u, &buf[5]);
  float32ToLeBytes(l, &buf[9]);
  float32ToLeBytes(l, &buf[13]);
  uint8_t x = 0;
  for (int i = 0; i < 17; ++i)
    x ^= buf[i];
  buf[17] = x;
  buf[18] = 0x55;
  port.write(buf, 19);
}

void sendFrame(float laserCm, float ultraCm) {
  if (g_txMode == TX_USB_TTL) {
    writeBinarySensorFrame(Serial, ultraCm, laserCm, true);
  } else {
    writeBinarySensorFrame(Serial1, ultraCm, laserCm, true);
  }
}

void setup() {
  Serial.begin(9600);
  Serial1.begin(115200);

  Serial.println(F("Arduino dual transport: binary frames only."));
  Serial.println(F("Send: MODE USB or MODE RADIO"));
}

void loop() {
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    cmd.toUpperCase();
    if (cmd == "MODE USB") {
      g_txMode = TX_USB_TTL;
      Serial.println(F("Switched to USB to TTL mode."));
    } else if (cmd == "MODE RADIO") {
      g_txMode = TX_DATA_RADIO;
      Serial.println(F("Switched to DataRadio mode."));
    }
  }

  const unsigned long now = millis();
  if (now - g_lastSendMs >= SEND_INTERVAL_MS) {
    g_lastSendMs = now;

    g_demoLaser += 0.2f * g_dir;
    g_demoUltra += 0.3f * g_dir;
    if (g_demoLaser > 60.0f || g_demoLaser < 40.0f) {
      g_dir = -g_dir;
    }

    sendFrame(g_demoLaser, g_demoUltra);
  }
}
