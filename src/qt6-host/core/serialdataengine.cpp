#include "serialdataengine.h"
#include "serialframeparser.h"
#include "winserialreader.h"

#include <QtGlobal>
#include <algorithm>
#include <cmath>

namespace {

// 与下位机量程大致一致：明显坏帧直接丢弃（不 emit、不喂滤波），避免卡尔曼状态被拉到错误平台
constexpr float kUltraMinCm = 1.5f;
constexpr float kUltraMaxCm = 500.f;
constexpr float kLaserMinCm = 1.5f;
constexpr float kLaserMaxCm = 150.f;
constexpr float kUltraLaserMaxDiffCm = 75.f;

bool samplePairPlausible(float ultraCm, float laserCm)
{
    if (!std::isfinite(ultraCm) || !std::isfinite(laserCm))
        return false;
    if (ultraCm < kUltraMinCm || ultraCm > kUltraMaxCm)
        return false;
    if (laserCm < kLaserMinCm || laserCm > kLaserMaxCm)
        return false;
    const float d = std::fabs(laserCm - ultraCm);
    if (d > kUltraLaserMaxDiffCm)
        return false;
    return true;
}

} // namespace

SerialDataEngine::SerialDataEngine(QObject *parent)
    : QObject(parent)
    , m_reader(new WinSerialReader(this))
    , m_parser(new SerialFrameParser(2048, this))
    , m_akfU(0.01f, 1.0f, 0.95f)
    , m_akfL(0.01f, 1.0f, 0.95f)
    , m_oefU(0.1f, 1.5f, 1.0f, 0.007f)
    , m_oefL(0.1f, 1.5f, 1.0f, 0.007f)
    , m_rhfU(0.01f, 1.0f, 0.10f, 2.5f)
    , m_rhfL(0.01f, 1.0f, 0.10f, 2.5f)
    , m_textLineRegex(QString::fromUtf8(
          "超声波[^0-9+\\-]*([+\\-]?\\d+(?:\\.\\d+)?)\\D+激光[^0-9+\\-]*([+\\-]?\\d+(?:\\.\\d+)?)"))
{
    connect(m_reader, &WinSerialReader::bytesReceived, this, &SerialDataEngine::onBytesReceived, Qt::QueuedConnection);
    connect(m_reader, &WinSerialReader::fatalError, this, &SerialDataEngine::onFatalError, Qt::QueuedConnection);

    connect(m_parser, &SerialFrameParser::frameDecoded, this, [this](float raw, float filtered, float tofRaw, float tofFilt) {
        processFrame(raw, filtered, tofRaw, tofFilt);
    });

    // 激光通道更易被历史野值拖偏：略紧的创新门限；超声可略松
    m_akfL.setInnovationGateSigma(4.0f);
    m_akfU.setInnovationGateSigma(6.0f);

    resetFilters();
}

SerialDataEngine::~SerialDataEngine()
{
    close();
}

bool SerialDataEngine::open(const QString &portName, int baudRate, TransportMode mode, QString *errorOut)
{
    close();
    resetFilters();
    if (m_parser) m_parser->reset();
    m_textLineBuffer.clear();
    m_rawRxBytes = 0;
    m_transportMode = mode;
    m_dtTimer.invalidate();
    m_lastTickMs = 0;

    if (!m_reader->openPort(portName, baudRate, errorOut)) {
        const QString msg = errorOut ? *errorOut : QString::fromUtf8("打开串口失败");
        emit connectionStateChanged(false, msg);
        return false;
    }
    emit connectionStateChanged(true, QString::fromUtf8("已打开 %1 @ %2").arg(portName).arg(baudRate));
    return true;
}

void SerialDataEngine::close()
{
    if (m_reader && m_reader->isOpen()) {
        m_reader->closePort();
        emit connectionStateChanged(false, QString());
    }
}

bool SerialDataEngine::isOpen() const
{
    return m_reader && m_reader->isOpen();
}

void SerialDataEngine::resetFilters()
{
    m_akfU.reset(false);
    m_akfL.reset(false);

    m_oefU.reset();
    m_oefL.reset();

    m_rhfU.reset();
    m_rhfL.reset();
}

void SerialDataEngine::onBytesReceived(const QByteArray &bytes)
{
    m_rawRxBytes += bytes.size();

    // 兼容 Arduino 文本串口输出：
    // "超声波传感器采集数据:55.1cm 激光传感器采集数据:6.6cm"
    parseTextProtocol(bytes);

    // USB 与数传使用同一套二进制帧（SerialFrameParser：0xAA + 4×float LE + XOR + 0x55）
    if (m_parser)
        m_parser->append(bytes);
}

void SerialDataEngine::onFatalError(const QString &message)
{
    close();
    emit connectionStateChanged(false, message);
}

void SerialDataEngine::processFrame(float raw, float filtered, float tofRaw, float tofFiltered)
{
    if (!samplePairPlausible(raw, tofRaw)) {
        // 坏帧：不 emit、不喂滤波；激光 AKF 必清（避免状态长期卡在 900+）；超声仅在不合法时清
        m_akfL.reset(false);
        const bool ultraBad =
            !std::isfinite(raw) || raw < kUltraMinCm || raw > kUltraMaxCm;
        if (ultraBad)
            m_akfU.reset(false);
        return;
    }

    // 动态估计 dt，喂给 OneEuro（更贴近真实采样率）
    const qint64 nowMs = m_dtTimer.isValid() ? m_dtTimer.elapsed() : 0;
    float dtSec = 0.1f;
    if (m_dtTimer.isValid()) {
        const qint64 delta = nowMs - m_lastTickMs;
        if (delta > 0) dtSec = std::clamp(static_cast<float>(delta) / 1000.0f, 0.005f, 1.0f);
    } else {
        m_dtTimer.start();
    }
    m_lastTickMs = nowMs;
    m_oefU.setDt(dtSec);
    m_oefL.setDt(dtSec);

    // 显示“发送端给出的 raw / filtered / tofRaw / tofFilt”
    FilterDataPacket packet;
    packet.rawUltrasonicMeas = static_cast<double>(raw);
    packet.rawUltrasonicFilt = static_cast<double>(filtered);
    packet.rawLaserMeas = static_cast<double>(tofRaw);
    packet.rawLaserFilt = static_cast<double>(tofFiltered);

    // 三种滤波：以测量值（raw / tofRaw）作为输入，输出到对应模块字段
    const double uMeas = packet.rawUltrasonicMeas;
    const double lMeas = packet.rawLaserMeas;

    // 1) 自适应卡尔曼（AKF）
    packet.kalmanUltrasonicMeas = uMeas;
    packet.kalmanLaserMeas = lMeas;
    packet.kalmanUltrasonicFilt = static_cast<double>(m_akfU.update(static_cast<float>(uMeas)));
    packet.kalmanLaserFilt = static_cast<double>(m_akfL.update(static_cast<float>(lMeas)));

    // 2) 自适应低通（OneEuro-like）
    packet.adaptiveUltrasonicMeas = uMeas;
    packet.adaptiveLaserMeas = lMeas;
    packet.adaptiveUltrasonicFilt = static_cast<double>(m_oefU.update(static_cast<float>(uMeas)));
    packet.adaptiveLaserFilt = static_cast<double>(m_oefL.update(static_cast<float>(lMeas)));

    // 3) Robust H∞
    packet.hinfUltrasonicMeas = uMeas;
    packet.hinfLaserMeas = lMeas;
    packet.hinfUltrasonicFilt = static_cast<double>(m_rhfU.update(static_cast<float>(uMeas)));
    packet.hinfLaserFilt = static_cast<double>(m_rhfL.update(static_cast<float>(lMeas)));

    emit dataUpdated(packet);
}

void SerialDataEngine::parseTextProtocol(const QByteArray &bytes)
{
    if (bytes.isEmpty()) return;

    m_textLineBuffer.append(bytes);
    // 防止极端情况下缓存无限增长
    if (m_textLineBuffer.size() > 4096) {
        m_textLineBuffer = m_textLineBuffer.right(2048);
    }

    while (true) {
        int lineEnd = m_textLineBuffer.indexOf('\n');
        if (lineEnd < 0) break;

        QByteArray line = m_textLineBuffer.left(lineEnd).trimmed();
        m_textLineBuffer.remove(0, lineEnd + 1);
        if (line.isEmpty()) continue;

        const QString text = QString::fromUtf8(line);
        const QRegularExpressionMatch match = m_textLineRegex.match(text);
        if (!match.hasMatch()) continue;

        bool okUltra = false;
        bool okLaser = false;
        const float ultra = match.capturedView(1).toFloat(&okUltra);
        const float laser = match.capturedView(2).toFloat(&okLaser);
        if (!okUltra || !okLaser) continue;

        // 文本协议没有单独滤波字段：测量值同步填充到 raw/filtered 两列
        processFrame(ultra, ultra, laser, laser);
    }
}

