#include "connectionwidget.h"
#include "core/winserialreader.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QLabel>
#include <QMessageBox>
#include <QSettings>
#include <QSet>
#include <QApplication>
#include <QEventLoop>

ConnectionWidget::ConnectionWidget(QWidget *parent)
    : QWidget(parent)
    , m_comboPort(nullptr)
    , m_comboBaudRate(nullptr)
    , m_comboTransportMode(nullptr)
    , m_btnConnect(nullptr)
    , m_btnRefresh(nullptr)
    , m_btnProbeBaud(nullptr)
    , m_iconLabel(nullptr)
    , m_connected(false)
{
    setupUi();
}

ConnectionWidget::~ConnectionWidget()
{
}

void ConnectionWidget::setupUi()
{
    QVBoxLayout *mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(40, 40, 40, 40);
    mainLayout->setSpacing(24);

    m_iconLabel = new QLabel(this);
    m_iconLabel->setFixedSize(80, 80);
    m_iconLabel->setAlignment(Qt::AlignCenter);
    m_iconLabel->setStyleSheet(
        "QLabel {"
        "  background-color: #333333;"
        "  border-radius: 40px;"
        "  color: white;"
        "  font-size: 34px;"
        "}"
    );
    m_iconLabel->setText(QString::fromUtf8("USB"));
    mainLayout->addWidget(m_iconLabel, 0, Qt::AlignHCenter);
    mainLayout->addSpacing(20);

    QWidget *formWidget = new QWidget(this);
    QVBoxLayout *formLayout = new QVBoxLayout(formWidget);
    formLayout->setSpacing(16);

    QHBoxLayout *portLayout = new QHBoxLayout();
    portLayout->addWidget(new QLabel(QString::fromUtf8("串口号:"), this));
    m_comboPort = new QComboBox(this);
    m_comboPort->setMinimumWidth(200);
    m_comboPort->setEditable(true);
    portLayout->addWidget(m_comboPort);

    m_btnRefresh = new QPushButton(QString::fromUtf8("刷新"), this);
    m_btnRefresh->setMinimumHeight(30);
    m_btnRefresh->setStyleSheet(
        "QPushButton {"
        "  background-color: #E0E0E0;"
        "  border: none;"
        "  border-radius: 6px;"
        "  padding: 6px 10px;"
        "  font-size: 14px;"
        "}"
        "QPushButton:hover { background-color: #D0D0D0; }"
    );
    portLayout->addWidget(m_btnRefresh);
    m_btnProbeBaud = new QPushButton(QString::fromUtf8("探测波特率"), this);
    m_btnProbeBaud->setMinimumHeight(30);
    m_btnProbeBaud->setStyleSheet(m_btnRefresh->styleSheet());
    portLayout->addWidget(m_btnProbeBaud);
    portLayout->addStretch();
    formLayout->addLayout(portLayout);

    QHBoxLayout *baudLayout = new QHBoxLayout();
    baudLayout->addWidget(new QLabel(QString::fromUtf8("波特率:"), this));
    m_comboBaudRate = new QComboBox(this);
    m_comboBaudRate->setMinimumWidth(200);
    m_comboBaudRate->setEditable(true);
    baudLayout->addWidget(m_comboBaudRate);
    baudLayout->addStretch();
    formLayout->addLayout(baudLayout);

    QHBoxLayout *modeLayout = new QHBoxLayout();
    modeLayout->addWidget(new QLabel(QString::fromUtf8("传输模式:"), this));
    m_comboTransportMode = new QComboBox(this);
    m_comboTransportMode->setMinimumWidth(200);
    m_comboTransportMode->addItem(QString::fromUtf8("USB to TTL"), static_cast<int>(TransportMode::UsbToTtl));
    m_comboTransportMode->addItem(QString::fromUtf8("数传模块"), static_cast<int>(TransportMode::DataRadio));
    modeLayout->addWidget(m_comboTransportMode);
    modeLayout->addStretch();
    formLayout->addLayout(modeLayout);

    QLabel *hint = new QLabel(
        QString::fromUtf8("提示：USB 与数传均为 19 字节二进制帧（0xAA…0x55）。数传波特率须与微空助手「地面模块 USB 串口」及 Arduino 中 RADIO_SERIAL_BAUD 一致；可用「探测波特率」在未连接时粗测哪档有字节。"),
        this);
    hint->setWordWrap(true);
    hint->setStyleSheet(QString::fromUtf8("color:#555;font-size:13px;"));
    formLayout->addWidget(hint);

    fillAvailablePorts();

    mainLayout->addWidget(formWidget, 0, Qt::AlignHCenter);

    m_btnConnect = new QPushButton(QString::fromUtf8("连接"), this);
    m_btnConnect->setMinimumWidth(120);
    m_btnConnect->setMinimumHeight(36);
    m_btnConnect->setStyleSheet(
        "QPushButton {"
        "  background-color: #E0E0E0;"
        "  color: black;"
        "  border: none;"
        "  border-radius: 8px;"
        "  font-size: 15px;"
        "}"
        "QPushButton:hover { background-color: #D0D0D0; }"
    );
    mainLayout->addWidget(m_btnConnect, 0, Qt::AlignHCenter);
    mainLayout->addStretch();

    connect(m_btnConnect, &QPushButton::clicked, this, &ConnectionWidget::onConnectClicked);
    connect(m_btnRefresh, &QPushButton::clicked, this, &ConnectionWidget::onRefreshClicked);
    connect(m_btnProbeBaud, &QPushButton::clicked, this, &ConnectionWidget::onProbeBaudClicked);
    connect(m_comboTransportMode, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ConnectionWidget::onTransportModeChanged);

    // 默认数传模块；波特率默认 115200，可手改并与固件/助手对齐
    m_comboTransportMode->setCurrentIndex(1);
    syncBaudToTransportMode();
}

void ConnectionWidget::fillAvailablePorts()
{
    m_comboPort->clear();
    QSet<QString> portsSet;

#ifdef Q_OS_WIN
    // 从注册表枚举串口：HKEY_LOCAL_MACHINE\HARDWARE\DEVICEMAP\SERIALCOMM
    QSettings reg(QString::fromUtf8("HKEY_LOCAL_MACHINE\\HARDWARE\\DEVICEMAP\\SERIALCOMM"), QSettings::NativeFormat);
    const QStringList keys = reg.allKeys();
    for (const QString &k : keys) {
        const QString port = reg.value(k).toString().trimmed();
        if (!port.isEmpty()) portsSet.insert(port);
    }
#endif

    // 兜底：常见 COM1..COM20（VSPD 虚拟串口也常用）
    for (int i = 1; i <= 20; ++i) {
        portsSet.insert(QString::fromUtf8("COM%1").arg(i));
    }

    QStringList ports = QStringList(portsSet.begin(), portsSet.end());
    std::sort(ports.begin(), ports.end(), [](const QString &a, const QString &b) {
        auto toNum = [](const QString &s) -> int {
            QString t = s.toUpper().trimmed();
            if (!t.startsWith("COM")) return 100000;
            bool ok = false;
            int n = t.mid(3).toInt(&ok);
            return ok ? n : 100000;
        };
        return toNum(a) < toNum(b);
    });
    for (const QString &p : ports) {
        m_comboPort->addItem(p, p);
    }

    // 若系统没枚举到，也允许手动输入（例如 VSPD 虚拟串口）
    if (m_comboPort->count() == 0) {
        m_comboPort->addItem(QString::fromUtf8("COM3"), QString::fromUtf8("COM3"));
        m_comboPort->addItem(QString::fromUtf8("COM2"), QString::fromUtf8("COM2"));
        m_comboPort->addItem(QString::fromUtf8("COM1"), QString::fromUtf8("COM1"));
    }

    // 默认选中 COM3（若存在）
    for (int i = 0; i < m_comboPort->count(); ++i) {
        const QString pn = m_comboPort->itemData(i).toString();
        if (pn.compare(QString::fromUtf8("COM3"), Qt::CaseInsensitive) == 0) {
            m_comboPort->setCurrentIndex(i);
            break;
        }
    }

    m_comboBaudRate->clear();
    m_comboBaudRate->addItem(QString::fromUtf8("9600"));
    m_comboBaudRate->addItem(QString::fromUtf8("19200"));
    m_comboBaudRate->addItem(QString::fromUtf8("38400"));
    m_comboBaudRate->addItem(QString::fromUtf8("57600"));
    m_comboBaudRate->addItem(QString::fromUtf8("115200"));
    m_comboBaudRate->addItem(QString::fromUtf8("230400"));
    m_comboBaudRate->addItem(QString::fromUtf8("460800"));
    m_comboBaudRate->addItem(QString::fromUtf8("921600")); // LR24 透传 20K/s 时手册要求
    m_comboBaudRate->setCurrentIndex(0);

    // 按当前传输模式给出默认波特率（可编辑）
    syncBaudToTransportMode();
}

void ConnectionWidget::syncBaudToTransportMode()
{
    if (!m_comboBaudRate || !m_comboTransportMode) {
        return;
    }

    const TransportMode mode = selectedTransportMode();
    const QString target = (mode == TransportMode::DataRadio)
        ? QString::fromUtf8("115200")
        : QString::fromUtf8("9600");

    for (int i = 0; i < m_comboBaudRate->count(); ++i) {
        if (m_comboBaudRate->itemText(i) == target) {
            m_comboBaudRate->setCurrentIndex(i);
            break;
        }
    }
    m_comboBaudRate->setCurrentText(target);

    m_comboBaudRate->setEnabled(!m_connected);
}

void ConnectionWidget::onTransportModeChanged()
{
    syncBaudToTransportMode();
}

void ConnectionWidget::onConnectClicked()
{
    if (m_connected) {
        emit disconnectRequested();
        return;
    }
    emit connectRequested(selectedPortName(), selectedBaudRate(), selectedTransportMode());
}

void ConnectionWidget::onRefreshClicked()
{
    if (m_connected) return;
    fillAvailablePorts();
    emit refreshRequested();
}

void ConnectionWidget::onProbeBaudClicked()
{
    if (m_connected) {
        QMessageBox::information(this, QString::fromUtf8("波特率探测"),
                                   QString::fromUtf8("请先点击「断开」，再探测（探测会短暂独占串口）。"));
        return;
    }
    const QString port = selectedPortName();
    if (port.isEmpty()) {
        QMessageBox::warning(this, QString::fromUtf8("波特率探测"), QString::fromUtf8("请选择串口号。"));
        return;
    }

    const QList<int> bauds = {9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600};
    const int perMs = 450;

    QString lines;
    int bestBaud = -1;
    qint64 bestCount = 0;

    for (int baud : bauds) {
        QString err;
        const qint64 n = WinSerialReader::probeRxByteCount(port, baud, perMs, &err);
        if (n >= 0) {
            lines += QString::fromUtf8("%1：%2 字节\n").arg(baud).arg(n);
            if (n > bestCount) {
                bestCount = n;
                bestBaud = baud;
            }
        } else {
            lines += QString::fromUtf8("%1：失败 %2\n").arg(baud).arg(err);
        }
        QApplication::processEvents(QEventLoop::ExcludeUserInputEvents);
    }

    QString summary = QString::fromUtf8("端口 %1（每档监听约 %2 ms）\n\n%3")
                          .arg(port)
                          .arg(perMs)
                          .arg(lines);
    if (bestBaud > 0 && bestCount > 0) {
        summary += QString::fromUtf8("\n接收字节最多的是 %1（%2 字节），已填入波特率下拉框。")
                        .arg(bestBaud)
                        .arg(bestCount);
        m_comboBaudRate->setCurrentText(QString::number(bestBaud));
    } else {
        summary += QString::fromUtf8(
            "\n各档均为 0：多为未选对 COM、地面数传未上电/未对频，或 Arduino 未在发；与 Qt 波特率无关时也会全 0。");
    }
    QMessageBox::information(this, QString::fromUtf8("波特率探测结果"), summary);
}

QString ConnectionWidget::selectedPortName() const
{
    const QVariant data = m_comboPort->currentData();
    if (data.isValid() && !data.toString().isEmpty())
        return data.toString();
    return m_comboPort->currentText().trimmed();
}

int ConnectionWidget::selectedBaudRate() const
{
    bool ok = false;
    int baud = m_comboBaudRate->currentText().trimmed().toInt(&ok);
    return ok ? baud : 115200;
}

ConnectionWidget::TransportMode ConnectionWidget::selectedTransportMode() const
{
    if (!m_comboTransportMode) {
        return TransportMode::UsbToTtl;
    }

    const int mode = m_comboTransportMode->currentData().toInt();
    if (mode == static_cast<int>(TransportMode::DataRadio)) {
        return TransportMode::DataRadio;
    }
    return TransportMode::UsbToTtl;
}

void ConnectionWidget::setConnected(bool connected)
{
    m_connected = connected;
    m_btnConnect->setText(connected ? QString::fromUtf8("断开") : QString::fromUtf8("连接"));
    m_comboPort->setEnabled(!connected);
    if (m_comboBaudRate)
        m_comboBaudRate->setEnabled(!connected);
    if (m_comboTransportMode) m_comboTransportMode->setEnabled(!connected);
    if (m_btnRefresh) m_btnRefresh->setEnabled(!connected);
    if (m_btnProbeBaud) m_btnProbeBaud->setEnabled(!connected);
}
