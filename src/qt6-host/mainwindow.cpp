#include "mainwindow.h"
#include "modules/connection/connectionwidget.h"
#include "modules/filterdata/filterdatawidget.h"
#include "modules/curveanalysis/curveanalysiswidget.h"
#include "modules/smarttuning/smarttuningwidget.h"
#include "core/serialdataengine.h"
#include "core/filterdatapacket.h"
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QFrame>
#include <QScrollArea>
#include <QStyle>
#include <QApplication>
#include <QMessageBox>

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
    , m_centralWidget(nullptr)
    , m_leftPanel(nullptr)
    , m_stackedWidget(nullptr)
    , m_btnConnection(nullptr)
    , m_btnFilterData(nullptr)
    , m_btnCurveAnalysis(nullptr)
    , m_btnSmartTuning(nullptr)
    , m_labelStatus(nullptr)
    , m_labelRecvData(nullptr)
    , m_labelRawBytes(nullptr)
    , m_labelDataFreq(nullptr)
    , m_connectionWidget(nullptr)
    , m_filterDataWidget(nullptr)
    , m_curveAnalysisWidget(nullptr)
    , m_smartTuningWidget(nullptr)
    , m_smartTuningPopupWidget(nullptr)
    , m_serialEngine(nullptr)
    , m_freqUpdateTimer(nullptr)
    , m_recvCount(0)
    , m_freqCount(0)
{
    setupUi();
}

MainWindow::~MainWindow()
{
}

void MainWindow::setupUi()
{
    setWindowTitle(QString::fromUtf8("滤波可视化平台"));
    setMinimumSize(900, 600);
    resize(1000, 650);

    QPalette pal = palette();
    pal.setColor(QPalette::Window, QColor(240, 240, 240));
    setPalette(pal);

    m_centralWidget = new QWidget(this);
    setCentralWidget(m_centralWidget);

    QHBoxLayout *mainLayout = new QHBoxLayout(m_centralWidget);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);

    setupLeftPanel();
    setupStackedWidget();

    mainLayout->addWidget(m_leftPanel);
    mainLayout->addWidget(m_stackedWidget, 1);
}

void MainWindow::setupLeftPanel()
{
    m_leftPanel = new QFrame(this);
    m_leftPanel->setFixedWidth(180);
    m_leftPanel->setFrameShape(QFrame::NoFrame);
    QPalette palPanel = m_leftPanel->palette();
    palPanel.setColor(QPalette::Window, QColor(230, 230, 230));
    m_leftPanel->setPalette(palPanel);
    m_leftPanel->setAutoFillBackground(true);

    QVBoxLayout *leftLayout = new QVBoxLayout(m_leftPanel);
    leftLayout->setContentsMargins(16, 24, 16, 16);
    leftLayout->setSpacing(12);

    QLabel *logo = new QLabel(m_leftPanel);
    logo->setFixedSize(48, 48);
    logo->setStyleSheet(
        "QLabel {"
        "  background-color: #2196F3;"
        "  border-radius: 24px;"
        "  border: none;"
        "}"
    );
    logo->setAlignment(Qt::AlignCenter);
    logo->setText(QString::fromUtf8(""));
    leftLayout->addWidget(logo, 0, Qt::AlignLeft);

    m_btnConnection = new QPushButton(QString::fromUtf8("通讯连接"), m_leftPanel);
    m_btnFilterData = new QPushButton(QString::fromUtf8("滤波数据"), m_leftPanel);
    m_btnCurveAnalysis = new QPushButton(QString::fromUtf8("曲线分析"), m_leftPanel);
    m_btnSmartTuning = new QPushButton(QString::fromUtf8("智能调参"), m_leftPanel);

    QString btnStyle =
        "QPushButton {"
        "  background-color: #E0E0E0;"
        "  color: black;"
        "  border: none;"
        "  border-radius: 8px;"
        "  padding: 10px 16px;"
        "  text-align: left;"
        "  font-size: 15px;"
        "}"
        "QPushButton:hover { background-color: #D0D0D0; }"
        "QPushButton:checked { background-color: #BDBDBD; }";
    m_btnConnection->setStyleSheet(btnStyle);
    m_btnFilterData->setStyleSheet(btnStyle);
    m_btnCurveAnalysis->setStyleSheet(btnStyle);
    m_btnSmartTuning->setStyleSheet(btnStyle);

    m_btnConnection->setCheckable(true);
    m_btnFilterData->setCheckable(true);
    m_btnCurveAnalysis->setCheckable(true);
    m_btnSmartTuning->setCheckable(true);

    m_btnConnection->setChecked(true);
    setActiveButton(m_btnConnection);

    leftLayout->addWidget(m_btnConnection);
    leftLayout->addWidget(m_btnFilterData);
    leftLayout->addWidget(m_btnCurveAnalysis);
    leftLayout->addWidget(m_btnSmartTuning);

    leftLayout->addStretch();

    m_labelStatus = new QLabel(QString::fromUtf8("未连接"), m_leftPanel);
    m_labelStatus->setStyleSheet("color: red; font-weight: bold;");
    leftLayout->addWidget(m_labelStatus);

    m_labelRecvData = new QLabel(QString::fromUtf8("接收数据: 0"), m_leftPanel);
    leftLayout->addWidget(m_labelRecvData);

    m_labelRawBytes = new QLabel(QString::fromUtf8("原始字节: 0"), m_leftPanel);
    m_labelRawBytes->setStyleSheet(QString::fromUtf8("color:#666;font-size:12px;"));
    m_labelRawBytes->setToolTip(QString::fromUtf8(
        "串口收到的总字节数（含未解码）。若为数传仍为 0，说明 PC 未收到无线数据或对频失败；若>0但接收数据仍为0，多半是格式不匹配。"));
    leftLayout->addWidget(m_labelRawBytes);

    m_labelDataFreq = new QLabel(QString::fromUtf8("数据频率: 0"), m_leftPanel);
    leftLayout->addWidget(m_labelDataFreq);

    connect(m_btnConnection, &QPushButton::clicked, this, &MainWindow::onConnectionClicked);
    connect(m_btnFilterData, &QPushButton::clicked, this, &MainWindow::onFilterDataClicked);
    connect(m_btnCurveAnalysis, &QPushButton::clicked, this, &MainWindow::onCurveAnalysisClicked);
    connect(m_btnSmartTuning, &QPushButton::clicked, this, &MainWindow::onSmartTuningClicked);
}

void MainWindow::setupStackedWidget()
{
    m_stackedWidget = new QStackedWidget(this);
    m_stackedWidget->setFrameShape(QFrame::NoFrame);
    QPalette palStack = m_stackedWidget->palette();
    palStack.setColor(QPalette::Window, QColor(245, 245, 245));
    m_stackedWidget->setPalette(palStack);
    m_stackedWidget->setAutoFillBackground(true);

    m_connectionWidget = new ConnectionWidget(this);
    m_filterDataWidget = new FilterDataWidget(this);
    m_curveAnalysisWidget = new CurveAnalysisWidget(this);
    m_smartTuningWidget = new SmartTuningWidget(this);

    m_stackedWidget->addWidget(m_connectionWidget);
    m_stackedWidget->addWidget(m_filterDataWidget);
    m_stackedWidget->addWidget(m_curveAnalysisWidget);
    m_stackedWidget->addWidget(m_smartTuningWidget);

    m_stackedWidget->setCurrentWidget(m_connectionWidget);

    m_serialEngine = new SerialDataEngine(this);
    m_freqUpdateTimer = new QTimer(this);
    m_freqUpdateTimer->setInterval(1000);
    connect(m_freqUpdateTimer, &QTimer::timeout, this, &MainWindow::updateStatusLabels);

    // 智能调参页接入串口数据与滤波器
    if (m_smartTuningWidget && m_serialEngine) {
        m_smartTuningWidget->setSerialEngine(m_serialEngine);
        connect(m_serialEngine, &SerialDataEngine::dataUpdated,
                m_smartTuningWidget, &SmartTuningWidget::onDataUpdated);
    }

    // 为智能调参弹出一个独立的智能调参窗口
    m_smartTuningPopupWidget = new SmartTuningWidget();
    m_smartTuningPopupWidget->setWindowTitle(QString::fromUtf8("AI辅助调参 - 独立视图"));
    m_smartTuningPopupWidget->resize(900, 600);
    if (m_serialEngine) {
        m_smartTuningPopupWidget->setSerialEngine(m_serialEngine);
    }

    connect(m_connectionWidget, &ConnectionWidget::connectRequested, this, &MainWindow::onConnectRequested);
    connect(m_connectionWidget, &ConnectionWidget::disconnectRequested, this, &MainWindow::onDisconnectRequested);

    connect(m_serialEngine, &SerialDataEngine::dataUpdated, m_filterDataWidget, &FilterDataWidget::onDataUpdated);
    connect(m_serialEngine, &SerialDataEngine::dataUpdated, m_curveAnalysisWidget, &CurveAnalysisWidget::onDataUpdated);
    if (m_smartTuningPopupWidget) {
        connect(m_serialEngine, &SerialDataEngine::dataUpdated,
                m_smartTuningPopupWidget, &SmartTuningWidget::onDataUpdated);
    }
    connect(m_serialEngine, &SerialDataEngine::dataUpdated, this, [this](const FilterDataPacket &) {
        m_recvCount++;
        m_freqCount++;
    });

    connect(m_serialEngine, &SerialDataEngine::connectionStateChanged, this, [this](bool connected, const QString &message) {
        if (!connected && !message.isEmpty()) {
            // 异常断开时提示
            if (m_connectionWidget && m_connectionWidget->isConnected()) {
                QMessageBox::warning(this, QString::fromUtf8("串口"), message);
            }
        }
        if (m_connectionWidget) m_connectionWidget->setConnected(connected);
        if (connected) {
            m_recvCount = 0;
            m_freqCount = 0;
            if (m_labelRawBytes)
                m_labelRawBytes->setText(QString::fromUtf8("原始字节: 0"));
            m_labelStatus->setText(QString::fromUtf8("已连接"));
            m_labelStatus->setStyleSheet("color: green; font-weight: bold;");
            m_curveAnalysisWidget->clearChart();
            m_freqUpdateTimer->start();
        } else {
            m_labelStatus->setText(QString::fromUtf8("未连接"));
            m_labelStatus->setStyleSheet("color: red; font-weight: bold;");
            m_freqUpdateTimer->stop();
            m_labelRecvData->setText(QString::fromUtf8("接收数据: %1").arg(m_recvCount));
            m_labelDataFreq->setText(QString::fromUtf8("数据频率: 0"));
            m_curveAnalysisWidget->clearChart();
        }
    });
}

void MainWindow::setActiveButton(QPushButton *active)
{
    m_btnConnection->setChecked(m_btnConnection == active);
    m_btnFilterData->setChecked(m_btnFilterData == active);
    m_btnCurveAnalysis->setChecked(m_btnCurveAnalysis == active);
    m_btnSmartTuning->setChecked(m_btnSmartTuning == active);
}

void MainWindow::onConnectionClicked()
{
    setActiveButton(m_btnConnection);
    m_stackedWidget->setCurrentWidget(m_connectionWidget);
}

void MainWindow::onFilterDataClicked()
{
    setActiveButton(m_btnFilterData);
    m_stackedWidget->setCurrentWidget(m_filterDataWidget);
}

void MainWindow::onCurveAnalysisClicked()
{
    setActiveButton(m_btnCurveAnalysis);
    m_stackedWidget->setCurrentWidget(m_curveAnalysisWidget);
}

void MainWindow::onSmartTuningClicked()
{
    setActiveButton(m_btnSmartTuning);
    m_stackedWidget->setCurrentWidget(m_smartTuningWidget);

    // 打开或前置独立的智能调参窗口，方便多开调参视图
    if (m_smartTuningPopupWidget) {
        m_smartTuningPopupWidget->show();
        m_smartTuningPopupWidget->raise();
        m_smartTuningPopupWidget->activateWindow();
    }
}

void MainWindow::onConnectRequested(const QString &portName, int baudRate, ConnectionWidget::TransportMode mode)
{
    SerialDataEngine::TransportMode serialMode = SerialDataEngine::TransportMode::UsbToTtl;
    if (mode == ConnectionWidget::TransportMode::DataRadio) {
        serialMode = SerialDataEngine::TransportMode::DataRadio;
    }

    QString err;
    if (!m_serialEngine->open(portName, baudRate, serialMode, &err)) {
        if (!err.isEmpty())
            QMessageBox::warning(this, QString::fromUtf8("串口连接失败"), err);
        if (m_connectionWidget) m_connectionWidget->setConnected(false);
        return;
    }
}

void MainWindow::onDisconnectRequested()
{
    if (m_serialEngine) m_serialEngine->close();
    if (m_connectionWidget) m_connectionWidget->setConnected(false);
}

void MainWindow::updateStatusLabels()
{
    m_labelRecvData->setText(QString::fromUtf8("接收数据: %1").arg(m_recvCount));
    if (m_serialEngine && m_labelRawBytes) {
        m_labelRawBytes->setText(QString::fromUtf8("原始字节: %1").arg(m_serialEngine->rawRxBytes()));
    }
    m_labelDataFreq->setText(QString::fromUtf8("数据频率: %1").arg(m_freqCount));
    m_freqCount = 0;
}
