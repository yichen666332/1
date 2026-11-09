#ifndef WINSERIALREADER_H
#define WINSERIALREADER_H

#include <QThread>
#include <QByteArray>
#include <atomic>

#ifdef Q_OS_WIN
#  include <windows.h>
#endif

class WinSerialReader : public QThread
{
    Q_OBJECT

public:
    explicit WinSerialReader(QObject *parent = nullptr);
    ~WinSerialReader() override;

    bool openPort(const QString &portName, int baudRate, QString *errorOut = nullptr);
    void closePort();
    bool isOpen() const;

    /// 同步探测：durationMs 内读到的字节总数。调用前须确保该串口未被本程序其它处打开。
    /// 返回值 <0 表示打开或读失败（errorOut 有说明）。
    static qint64 probeRxByteCount(const QString &portName, int baudRate, int durationMs, QString *errorOut = nullptr);

signals:
    void bytesReceived(const QByteArray &bytes);
    void fatalError(const QString &message);

protected:
    void run() override;

private:
#ifdef Q_OS_WIN
    static QString formatWinError(DWORD code);
    static QString toDevicePath(const QString &portName);
    bool configurePort(int baudRate, QString *errorOut);

    HANDLE m_handle = INVALID_HANDLE_VALUE;
    std::atomic_bool m_stop{false};
#endif
};

#endif // WINSERIALREADER_H
