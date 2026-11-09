#ifndef SERIALDATAENGINE_H
#define SERIALDATAENGINE_H

#include "filterdatapacket.h"
#include "filters/adaptivekalmanfilter.h"
#include "filters/oneeurolikefilter.h"
#include "filters/robusthinfinityfilter.h"

#include <QObject>
#include <QElapsedTimer>
#include <QByteArray>
#include <QRegularExpression>

class WinSerialReader;
class SerialFrameParser;

class SerialDataEngine : public QObject
{
    Q_OBJECT

public:
    enum class TransportMode {
        UsbToTtl,
        DataRadio
    };

    explicit SerialDataEngine(QObject *parent = nullptr);
    ~SerialDataEngine();

    bool open(const QString &portName, int baudRate, TransportMode mode = TransportMode::UsbToTtl, QString *errorOut = nullptr);
    void close();
    bool isOpen() const;

    /// 自打开串口以来从 WinSerialReader 收到的原始字节总数（不论是否解析成功）
    qint64 rawRxBytes() const { return m_rawRxBytes; }

    // 提供对内部滤波器的访问（给智能调参模块使用）
    AdaptiveKalmanFilter &akfUltrasonic() { return m_akfU; }
    AdaptiveKalmanFilter &akfLaser() { return m_akfL; }
    OneEuroLikeFilter   &oefUltrasonic() { return m_oefU; }
    OneEuroLikeFilter   &oefLaser() { return m_oefL; }
    RobustHInfinityFilter &rhfUltrasonic() { return m_rhfU; }
    RobustHInfinityFilter &rhfLaser() { return m_rhfL; }

signals:
    void dataUpdated(const FilterDataPacket &packet);
    void connectionStateChanged(bool connected, const QString &message);

private slots:
    void onBytesReceived(const QByteArray &bytes);
    void onFatalError(const QString &message);

private:
    void resetFilters();
    void processFrame(float raw, float filtered, float tofRaw, float tofFiltered);
    void parseTextProtocol(const QByteArray &bytes);

    WinSerialReader *m_reader;
    SerialFrameParser *m_parser;
    TransportMode m_transportMode = TransportMode::UsbToTtl;

    AdaptiveKalmanFilter m_akfU;
    AdaptiveKalmanFilter m_akfL;

    OneEuroLikeFilter m_oefU;
    OneEuroLikeFilter m_oefL;

    RobustHInfinityFilter m_rhfU;
    RobustHInfinityFilter m_rhfL;

    QElapsedTimer m_dtTimer;
    qint64 m_lastTickMs = 0;

    qint64 m_rawRxBytes = 0;
    QByteArray m_textLineBuffer;
    QRegularExpression m_textLineRegex;
};

#endif // SERIALDATAENGINE_H
