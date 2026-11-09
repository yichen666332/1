#include "winserialreader.h"

#include <QtGlobal>
#include <QElapsedTimer>
#include <QThread>

#ifdef Q_OS_WIN
#include <QString>
#endif

WinSerialReader::WinSerialReader(QObject *parent)
    : QThread(parent)
{
}

qint64 WinSerialReader::probeRxByteCount(const QString &portName, int baudRate, int durationMs, QString *errorOut)
{
#ifndef Q_OS_WIN
    if (errorOut)
        *errorOut = QString::fromUtf8("当前仅 Windows 支持串口探测。");
    return -1;
#else
    if (durationMs <= 0)
        durationMs = 200;

    const QString devicePath = toDevicePath(portName.trimmed());
    HANDLE h = CreateFileW(reinterpret_cast<LPCWSTR>(devicePath.utf16()),
                           GENERIC_READ | GENERIC_WRITE,
                           0,
                           nullptr,
                           OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL,
                           nullptr);
    if (h == INVALID_HANDLE_VALUE) {
        if (errorOut)
            *errorOut = QString::fromUtf8("打开 %1 失败：%2").arg(portName, formatWinError(GetLastError()));
        return -1;
    }

    DCB dcb;
    SecureZeroMemory(&dcb, sizeof(dcb));
    dcb.DCBlength = sizeof(dcb);
    if (!GetCommState(h, &dcb)) {
        if (errorOut)
            *errorOut = QString::fromUtf8("获取串口参数失败：%1").arg(formatWinError(GetLastError()));
        CloseHandle(h);
        return -1;
    }
    dcb.BaudRate = static_cast<DWORD>(baudRate);
    dcb.ByteSize = 8;
    dcb.Parity = NOPARITY;
    dcb.StopBits = ONESTOPBIT;
    dcb.fBinary = TRUE;
    dcb.fDtrControl = DTR_CONTROL_ENABLE;
    dcb.fRtsControl = RTS_CONTROL_ENABLE;
    if (!SetCommState(h, &dcb)) {
        if (errorOut)
            *errorOut = QString::fromUtf8("设置波特率失败：%1").arg(formatWinError(GetLastError()));
        CloseHandle(h);
        return -1;
    }

    COMMTIMEOUTS to;
    SecureZeroMemory(&to, sizeof(to));
    to.ReadIntervalTimeout = 20;
    to.ReadTotalTimeoutMultiplier = 2;
    to.ReadTotalTimeoutConstant = 20;
    if (!SetCommTimeouts(h, &to)) {
        if (errorOut)
            *errorOut = QString::fromUtf8("设置超时失败：%1").arg(formatWinError(GetLastError()));
        CloseHandle(h);
        return -1;
    }
    SetupComm(h, 64 * 1024, 64 * 1024);
    PurgeComm(h, PURGE_RXCLEAR | PURGE_TXCLEAR);

    qint64 total = 0;
    QElapsedTimer timer;
    timer.start();
    QByteArray chunk;
    chunk.resize(4096);
    while (timer.elapsed() < durationMs) {
        DWORD nRead = 0;
        if (!ReadFile(h, chunk.data(), static_cast<DWORD>(chunk.size()), &nRead, nullptr)) {
            if (errorOut)
                *errorOut = QString::fromUtf8("读取失败：%1").arg(formatWinError(GetLastError()));
            CloseHandle(h);
            return -1;
        }
        total += static_cast<qint64>(nRead);
        if (nRead == 0)
            QThread::msleep(2);
    }
    CloseHandle(h);
    if (errorOut)
        errorOut->clear();
    return total;
#endif
}

WinSerialReader::~WinSerialReader()
{
    closePort();
    wait(1500);
}

bool WinSerialReader::isOpen() const
{
#ifdef Q_OS_WIN
    return m_handle != INVALID_HANDLE_VALUE;
#else
    return false;
#endif
}

bool WinSerialReader::openPort(const QString &portName, int baudRate, QString *errorOut)
{
#ifndef Q_OS_WIN
    const QString msg = QString::fromUtf8("当前实现仅支持 Windows 串口。");
    if (errorOut) *errorOut = msg;
    emit fatalError(msg);
    return false;
#else
    closePort();
    m_stop.store(false);

    const QString devicePath = toDevicePath(portName.trimmed());

    // 部分 CP210x/USB 串口仅用 GENERIC_READ 时缓冲行为异常；读写打开更兼容
    m_handle = CreateFileW(reinterpret_cast<LPCWSTR>(devicePath.utf16()),
                           GENERIC_READ | GENERIC_WRITE,
                           0,
                           nullptr,
                           OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL,
                           nullptr);

    if (m_handle == INVALID_HANDLE_VALUE) {
        const QString msg = QString::fromUtf8("打开串口失败 %1：%2").arg(portName, formatWinError(GetLastError()));
        if (errorOut) *errorOut = msg;
        emit fatalError(msg);
        return false;
    }

    if (!configurePort(baudRate, errorOut)) {
        CloseHandle(m_handle);
        m_handle = INVALID_HANDLE_VALUE;
        return false;
    }

    start();
    return true;
#endif
}

void WinSerialReader::closePort()
{
#ifdef Q_OS_WIN
    m_stop.store(true);
    if (m_handle != INVALID_HANDLE_VALUE) {
        CancelIoEx(m_handle, nullptr);
    }
    if (isRunning()) {
        wait(800);
    }
    if (m_handle != INVALID_HANDLE_VALUE) {
        CloseHandle(m_handle);
        m_handle = INVALID_HANDLE_VALUE;
    }
#endif
}

void WinSerialReader::run()
{
#ifdef Q_OS_WIN
    if (m_handle == INVALID_HANDLE_VALUE) return;

    QByteArray chunk;
    chunk.resize(4096);

    while (!m_stop.load()) {
        DWORD nRead = 0;
        BOOL ok = ReadFile(m_handle, chunk.data(), static_cast<DWORD>(chunk.size()), &nRead, nullptr);
        if (!ok) {
            DWORD err = GetLastError();
            if (m_stop.load()) break;
            emit fatalError(QString::fromUtf8("串口读取失败：%1").arg(formatWinError(err)));
            break;
        }

        if (nRead > 0) {
            emit bytesReceived(QByteArray(chunk.constData(), static_cast<int>(nRead)));
        } else {
            msleep(2);
        }
    }
#endif
}

#ifdef Q_OS_WIN
QString WinSerialReader::toDevicePath(const QString &portName)
{
    QString p = portName;
    // 允许用户输入 "COM2 (xxx)"，取前缀
    int sp = p.indexOf(' ');
    if (sp > 0) p = p.left(sp);
    p = p.trimmed();
    if (p.isEmpty()) p = QString::fromUtf8("COM2");
    if (p.startsWith("\\\\.\\")) return p;
    return QString::fromUtf8("\\\\.\\%1").arg(p);
}

QString WinSerialReader::formatWinError(DWORD code)
{
    wchar_t *buf = nullptr;
    DWORD flags = FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS;
    DWORD len = FormatMessageW(flags, nullptr, code, 0, reinterpret_cast<LPWSTR>(&buf), 0, nullptr);
    QString msg;
    if (len > 0 && buf) {
        msg = QString::fromWCharArray(buf).trimmed();
        LocalFree(buf);
    } else {
        msg = QString::fromUtf8("Windows 错误码 %1").arg(code);
    }
    return msg;
}

bool WinSerialReader::configurePort(int baudRate, QString *errorOut)
{
    if (m_handle == INVALID_HANDLE_VALUE) return false;

    DCB dcb;
    SecureZeroMemory(&dcb, sizeof(dcb));
    dcb.DCBlength = sizeof(dcb);
    if (!GetCommState(m_handle, &dcb)) {
        const QString msg = QString::fromUtf8("获取串口参数失败：%1").arg(formatWinError(GetLastError()));
        if (errorOut) *errorOut = msg;
        emit fatalError(msg);
        return false;
    }

    dcb.BaudRate = static_cast<DWORD>(baudRate);
    dcb.ByteSize = 8;
    dcb.Parity = NOPARITY;
    dcb.StopBits = ONESTOPBIT;
    dcb.fBinary = TRUE;
    dcb.fDtrControl = DTR_CONTROL_ENABLE;
    dcb.fRtsControl = RTS_CONTROL_ENABLE;

    if (!SetCommState(m_handle, &dcb)) {
        const QString msg = QString::fromUtf8("设置串口参数失败：%1").arg(formatWinError(GetLastError()));
        if (errorOut) *errorOut = msg;
        emit fatalError(msg);
        return false;
    }

    COMMTIMEOUTS to;
    SecureZeroMemory(&to, sizeof(to));
    to.ReadIntervalTimeout = 20;
    to.ReadTotalTimeoutMultiplier = 2;
    to.ReadTotalTimeoutConstant = 20;
    if (!SetCommTimeouts(m_handle, &to)) {
        const QString msg = QString::fromUtf8("设置串口超时失败：%1").arg(formatWinError(GetLastError()));
        if (errorOut) *errorOut = msg;
        emit fatalError(msg);
        return false;
    }

    SetupComm(m_handle, 64 * 1024, 64 * 1024);
    PurgeComm(m_handle, PURGE_RXCLEAR | PURGE_TXCLEAR);

    return true;
}
#endif

