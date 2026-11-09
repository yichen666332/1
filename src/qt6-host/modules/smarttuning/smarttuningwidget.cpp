#include "smarttuningwidget.h"
#include "core/serialdataengine.h"
#include "core/filterdatapacket.h"
#include "akf/akf_autotuner_qt.h"
#include "oef/oneeuro_autotuner_qt.h"
#include "rhf/robusthinf_autotuner_qt.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QScrollArea>
#include <QCoreApplication>
#include <QDir>
#include <QSignalBlocker>

SmartTuningWidget::SmartTuningWidget(QWidget *parent)
    : QWidget(parent)
    , m_deepseekClient(new DeepSeekClientQt(this))
    , m_logEdit(nullptr)
    , m_btnPing(nullptr)
    , m_hasDeepSeek(false)
{
    setupUi();
}

SmartTuningWidget::~SmartTuningWidget()
{
}

void SmartTuningWidget::setSerialEngine(SerialDataEngine *engine)
{
    m_engine = engine;
    if (!m_engine || !m_hasDeepSeek)
        return;

    // 以超声波通道为主进行调参，也可以根据需要扩展为双通道
    m_akfTuner = new AkfAutoTunerQt(m_engine->akfUltrasonic(), m_deepseekClient, AkfAutoTunerQt::Config(), this);
    m_oefTuner = new OneEuroAutoTunerQt(m_engine->oefUltrasonic(), m_deepseekClient, OneEuroAutoTunerQt::Config(), this);
    m_rhfTuner = new RobustHInfAutoTunerQt(m_engine->rhfUltrasonic(), m_deepseekClient, RobustHInfAutoTunerQt::Config(), this);

    connect(m_akfTuner, &AkfAutoTunerQt::logMessage, this, &SmartTuningWidget::appendLog);
    connect(m_oefTuner, &OneEuroAutoTunerQt::logMessage, this, &SmartTuningWidget::appendLog);
    connect(m_rhfTuner, &RobustHInfAutoTunerQt::logMessage, this, &SmartTuningWidget::appendLog);
    connect(m_akfTuner, &AkfAutoTunerQt::parametersApplied, this, [this](float q, float alpha) {
        const int idx = static_cast<int>(AlgoKalman);
        if (idx >= 0 && idx < m_paramPages.size()) {
            setSpinValueQuietly(m_paramPages[idx].spinRawQ, q);
            setSpinValueQuietly(m_paramPages[idx].spinRawAlpha, alpha);
        }
    });
    connect(m_oefTuner, &OneEuroAutoTunerQt::parametersApplied, this, [this](float minCutoff, float dCutoff, float beta) {
        const int idx = static_cast<int>(AlgoOneEuro);
        if (idx >= 0 && idx < m_paramPages.size()) {
            setSpinValueQuietly(m_paramPages[idx].spinRawQ, minCutoff);
            setSpinValueQuietly(m_paramPages[idx].spinFiltQ, dCutoff);
            setSpinValueQuietly(m_paramPages[idx].spinRawAlpha, beta);
        }
    });
    connect(m_rhfTuner, &RobustHInfAutoTunerQt::parametersApplied, this, [this](float q, float r, float gamma) {
        const int idx = static_cast<int>(AlgoHinf);
        if (idx >= 0 && idx < m_paramPages.size()) {
            setSpinValueQuietly(m_paramPages[idx].spinRawQ, q);
            setSpinValueQuietly(m_paramPages[idx].spinFiltQ, r);
            setSpinValueQuietly(m_paramPages[idx].spinRawAlpha, gamma);
        }
    });
    syncControlsFromAlgorithm(AlgoKalman);
    syncControlsFromAlgorithm(AlgoOneEuro);
    syncControlsFromAlgorithm(AlgoHinf);
}

void SmartTuningWidget::setupUi()
{
    QVBoxLayout *mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(20, 20, 20, 20);
    mainLayout->setSpacing(16);

    QLabel *title = new QLabel(QString::fromUtf8("AI辅助调参"), this);
    title->setStyleSheet("font-size: 19px; font-weight: bold;");
    mainLayout->addWidget(title);

    m_tabWidget = new QTabWidget(this);
    m_tabWidget->addTab(createParameterPage(AlgoKalman, QString::fromUtf8("卡尔曼滤波")), QString::fromUtf8("卡尔曼滤波"));
    m_tabWidget->addTab(createParameterPage(AlgoOneEuro, QString::fromUtf8("自适应低通滤波")), QString::fromUtf8("自适应低通滤波"));
    m_tabWidget->addTab(createParameterPage(AlgoHinf, QString::fromUtf8("H∞滤波")), QString::fromUtf8("H∞滤波"));
    mainLayout->addWidget(m_tabWidget);

    QHBoxLayout *aiToggleLayout = new QHBoxLayout();
    aiToggleLayout->setSpacing(8);

    m_btnEnableTune = new QPushButton(QString::fromUtf8("开启调参"), this);
    m_btnDisableTune = new QPushButton(QString::fromUtf8("关闭调参"), this);
    m_btnEnableTune->setMinimumHeight(32);
    m_btnDisableTune->setMinimumHeight(32);
    aiToggleLayout->addWidget(m_btnEnableTune);
    aiToggleLayout->addWidget(m_btnDisableTune);
    aiToggleLayout->addStretch();
    mainLayout->addLayout(aiToggleLayout);

    connect(m_btnEnableTune, &QPushButton::clicked, this, [this]() {
        setAiTuningEnabled(true);
    });
    connect(m_btnDisableTune, &QPushButton::clicked, this, [this]() {
        setAiTuningEnabled(false);
    });
    setAiTuningEnabled(true);

    QLabel *logTitle = new QLabel(QString::fromUtf8("日志 / 状态"), this);
    mainLayout->addWidget(logTitle);

    m_logEdit = new QTextEdit(this);
    m_logEdit->setReadOnly(true);
    m_logEdit->setMinimumHeight(140);
    m_logEdit->setStyleSheet(
        "QTextEdit {"
        "  background-color: #FAFAFA;"
        "  border: 1px solid #E0E0E0;"
        "  border-radius: 4px;"
        "  padding: 8px;"
        "  font-size: 14px;"
        "}"
    );
    mainLayout->addWidget(m_logEdit);

    // DeepSeek 连通性测试按钮
    m_btnPing = new QPushButton(QString::fromUtf8("测试 DeepSeek 连通性"), this);
    m_btnPing->setMinimumHeight(32);
    m_btnPing->setStyleSheet(
        "QPushButton {"
        "  background-color: #E0E0E0;"
        "  border: none;"
        "  border-radius: 6px;"
        "  padding: 6px 12px;"
        "  font-size: 14px;"
        "}"
        "QPushButton:hover { background-color: #D0D0D0; }"
    );
    mainLayout->addWidget(m_btnPing, 0, Qt::AlignLeft);

    // 直接在代码中设置 DeepSeek API Key（不再从 Deepseek.txt 读取）
    // 注意：实际开发中不要把真实密钥提交到版本库，这里仅为本地测试方便。
    m_deepseekClient->setApiKey(QString::fromUtf8("更换你的密钥"));
    m_hasDeepSeek = true;
    appendLog(QString::fromUtf8("<span style='color:green'>已在代码中配置 DeepSeek API Key。</span>"));

    connect(m_btnPing, &QPushButton::clicked, this, [this]() {
        if (!m_hasDeepSeek) {
            appendLog(QString::fromUtf8("<span style='color:red'>尚未配置 DeepSeek API Key，无法测试。</span>"));
            return;
        }
        appendLog(QString::fromUtf8("正在测试 DeepSeek 连通性 (chat/completions)..."));
        m_btnPing->setEnabled(false);
        m_deepseekClient->quickChat(QString::fromUtf8("请只回复两个大写字母：OK"),
                                    [this](bool ok, const QString &result) {
            m_btnPing->setEnabled(true);
            if (!ok) {
                appendLog(QString::fromUtf8("<span style='color:red'>DeepSeek 测试失败：%1</span>").arg(result.toHtmlEscaped()));
            } else {
                appendLog(QString::fromUtf8("<span style='color:green'>DeepSeek 返回: %1</span>")
                              .arg(result.toHtmlEscaped()));
            }
        });
    });
}

void SmartTuningWidget::onDataUpdated(const FilterDataPacket &packet)
{
    if (!m_engine || !m_hasDeepSeek || !m_aiTuningEnabled)
        return;

    const double raw = packet.rawUltrasonicMeas;

    if (m_akfTuner) {
        m_akfTuner->feed(raw, packet.kalmanUltrasonicFilt);
        m_akfTuner->maybeTune();
    }
    if (m_oefTuner) {
        m_oefTuner->feed(raw, packet.adaptiveUltrasonicFilt);
        m_oefTuner->maybeTune();
    }
    if (m_rhfTuner) {
        m_rhfTuner->feed(raw, packet.hinfUltrasonicFilt);
        m_rhfTuner->maybeTune();
    }
}

QWidget *SmartTuningWidget::createParameterPage(AlgorithmType algo, const QString &tabTitle)
{
    Q_UNUSED(tabTitle);
    QWidget *page = new QWidget(this);
    QVBoxLayout *pageLayout = new QVBoxLayout(page);
    pageLayout->setContentsMargins(16, 16, 16, 16);

    QGroupBox *paramGroup = new QGroupBox(QString::fromUtf8("参数设置"), page);
    QGridLayout *grid = new QGridLayout(paramGroup);

    int row = 0;

    QLabel *lRaw = new QLabel(QString::fromUtf8("Raw:"), page);
    QLabel *lQ1 = new QLabel(QString::fromUtf8("Q:"), page);
    QDoubleSpinBox *spinRawQ = new QDoubleSpinBox(page);
    spinRawQ->setRange(0.0, 1.0);
    spinRawQ->setDecimals(6);
    spinRawQ->setSingleStep(0.001);
    spinRawQ->setValue(0.01);
    QLabel *lAlpha1 = new QLabel(QString::fromUtf8("Alpha:"), page);
    QDoubleSpinBox *spinRawAlpha = new QDoubleSpinBox(page);
    spinRawAlpha->setRange(0.0, 1.0);
    spinRawAlpha->setDecimals(3);
    spinRawAlpha->setSingleStep(0.01);
    spinRawAlpha->setValue(0.95);
    QPushButton *btnReset = new QPushButton(QString::fromUtf8("重置"), page);

    grid->addWidget(lRaw, row, 0);
    grid->addWidget(lQ1, row, 1);
    grid->addWidget(spinRawQ, row, 2);
    grid->addWidget(lAlpha1, row, 3);
    grid->addWidget(spinRawAlpha, row, 4);
    grid->addWidget(btnReset, row, 5);
    row++;

    QLabel *lTuneSel = new QLabel(QString::fromUtf8("调参选择:"), page);
    QComboBox *comboTune = new QComboBox(page);
    comboTune->addItem(QString::fromUtf8("Raw"));
    comboTune->addItem(QString::fromUtf8("Filt"));
    QLabel *lFilt = new QLabel(QString::fromUtf8("Filt:"), page);
    QLabel *lQ2 = new QLabel(QString::fromUtf8("Q:"), page);
    QDoubleSpinBox *spinFiltQ = new QDoubleSpinBox(page);
    spinFiltQ->setRange(0.0, 1.0);
    spinFiltQ->setDecimals(6);
    spinFiltQ->setValue(0.0);
    QLabel *lAlpha2 = new QLabel(QString::fromUtf8("Alpha:"), page);
    QDoubleSpinBox *spinFiltAlpha = new QDoubleSpinBox(page);
    spinFiltAlpha->setRange(0.0, 1.0);
    spinFiltAlpha->setDecimals(3);
    spinFiltAlpha->setValue(0.7);

    grid->addWidget(lTuneSel, row, 0);
    grid->addWidget(comboTune, row, 1);
    grid->addWidget(lFilt, row, 2);
    grid->addWidget(lQ2, row, 3);
    grid->addWidget(spinFiltQ, row, 4);
    grid->addWidget(lAlpha2, row, 5);
    grid->addWidget(spinFiltAlpha, row, 6);
    row++;

    QLabel *lTofRaw = new QLabel(QString::fromUtf8("TofRaw:"), page);
    QLabel *lQ3 = new QLabel(QString::fromUtf8("Q:"), page);
    QDoubleSpinBox *spinTofRawQ = new QDoubleSpinBox(page);
    spinTofRawQ->setRange(0.0, 1.0);
    spinTofRawQ->setDecimals(6);
    spinTofRawQ->setValue(0.0);
    QLabel *lAlpha3 = new QLabel(QString::fromUtf8("Alpha:"), page);
    QDoubleSpinBox *spinTofRawAlpha = new QDoubleSpinBox(page);
    spinTofRawAlpha->setRange(0.0, 1.0);
    spinTofRawAlpha->setDecimals(3);
    spinTofRawAlpha->setValue(0.7);
    QPushButton *btnAITune = new QPushButton(QString::fromUtf8("AI调参"), page);

    grid->addWidget(lTofRaw, row, 0);
    grid->addWidget(lQ3, row, 1);
    grid->addWidget(spinTofRawQ, row, 2);
    grid->addWidget(lAlpha3, row, 3);
    grid->addWidget(spinTofRawAlpha, row, 4);
    grid->addWidget(btnAITune, row, 5);
    row++;

    QLabel *lTofFilt = new QLabel(QString::fromUtf8("TofFilt:"), page);
    QLabel *lQ4 = new QLabel(QString::fromUtf8("Q:"), page);
    QDoubleSpinBox *spinTofFiltQ = new QDoubleSpinBox(page);
    spinTofFiltQ->setRange(0.0, 1.0);
    spinTofFiltQ->setDecimals(6);
    spinTofFiltQ->setValue(0.0);
    QLabel *lAlpha4 = new QLabel(QString::fromUtf8("Alpha:"), page);
    QDoubleSpinBox *spinTofFiltAlpha = new QDoubleSpinBox(page);
    spinTofFiltAlpha->setRange(0.0, 1.0);
    spinTofFiltAlpha->setDecimals(3);
    spinTofFiltAlpha->setValue(0.7);

    grid->addWidget(lTofFilt, row, 0);
    grid->addWidget(lQ4, row, 1);
    grid->addWidget(spinTofFiltQ, row, 2);
    grid->addWidget(lAlpha4, row, 3);
    grid->addWidget(spinTofFiltAlpha, row, 4);

    pageLayout->addWidget(paramGroup);

    ParamControls controls;
    controls.spinRawQ = spinRawQ;
    controls.spinRawAlpha = spinRawAlpha;
    controls.spinFiltQ = spinFiltQ;
    controls.spinFiltAlpha = spinFiltAlpha;
    controls.spinTofRawQ = spinTofRawQ;
    controls.spinTofRawAlpha = spinTofRawAlpha;
    controls.spinTofFiltQ = spinTofFiltQ;
    controls.spinTofFiltAlpha = spinTofFiltAlpha;
    controls.btnReset = btnReset;
    controls.btnAITune = btnAITune;
    bindParameterControls(algo, controls);
    return page;
}

void SmartTuningWidget::bindParameterControls(AlgorithmType algo, const ParamControls &controls)
{
    const int idx = static_cast<int>(algo);
    if (m_paramPages.size() <= idx) m_paramPages.resize(idx + 1);
    m_paramPages[idx] = controls;

    auto bindSpin = [this, algo](QDoubleSpinBox *spin) {
        if (!spin) return;
        connect(spin, qOverload<double>(&QDoubleSpinBox::valueChanged), this, [this, algo](double) {
            applyManualParameters(algo);
        });
    };
    bindSpin(controls.spinRawQ);
    bindSpin(controls.spinRawAlpha);
    bindSpin(controls.spinFiltQ);
    bindSpin(controls.spinFiltAlpha);
    bindSpin(controls.spinTofRawQ);
    bindSpin(controls.spinTofRawAlpha);
    bindSpin(controls.spinTofFiltQ);
    bindSpin(controls.spinTofFiltAlpha);

    if (controls.btnAITune) {
        connect(controls.btnAITune, &QPushButton::clicked, this, [this, algo]() {
            triggerAiTune(algo);
        });
    }
    if (controls.btnReset) {
        connect(controls.btnReset, &QPushButton::clicked, this, [this, algo]() {
            resetAlgorithmState(algo);
        });
    }
}

void SmartTuningWidget::applyManualParameters(AlgorithmType algo)
{
    if (!m_engine) return;
    const int idx = static_cast<int>(algo);
    if (idx < 0 || idx >= m_paramPages.size()) return;
    const ParamControls &c = m_paramPages[idx];

    const float uRawQ = c.spinRawQ ? static_cast<float>(c.spinRawQ->value()) : 0.01f;
    const float uRawAlpha = c.spinRawAlpha ? static_cast<float>(c.spinRawAlpha->value()) : 0.95f;
    const float uFiltQ = c.spinFiltQ ? static_cast<float>(c.spinFiltQ->value()) : 0.0f;
    const float uFiltAlpha = c.spinFiltAlpha ? static_cast<float>(c.spinFiltAlpha->value()) : 0.7f;

    const float tRawQ = c.spinTofRawQ ? static_cast<float>(c.spinTofRawQ->value()) : 0.01f;
    const float tRawAlpha = c.spinTofRawAlpha ? static_cast<float>(c.spinTofRawAlpha->value()) : 0.95f;
    const float tFiltQ = c.spinTofFiltQ ? static_cast<float>(c.spinTofFiltQ->value()) : 0.0f;

    if (algo == AlgoKalman) {
        m_engine->akfUltrasonic().setParameters(uRawQ, uRawAlpha);
        m_engine->akfLaser().setParameters(tRawQ, tRawAlpha);
        appendLog(QString::fromUtf8("卡尔曼参数已更新（仅卡尔曼生效）。"));
        return;
    }
    if (algo == AlgoOneEuro) {
        m_engine->oefUltrasonic().setParameters(uRawQ, uFiltQ, uRawAlpha, false);
        m_engine->oefLaser().setParameters(tRawQ, tFiltQ, tRawAlpha, false);
        appendLog(QString::fromUtf8("自适应低通参数已更新（仅自适应低通生效）。"));
        return;
    }
    if (algo == AlgoHinf) {
        m_engine->rhfUltrasonic().setParameters(uRawQ, uFiltQ, uRawAlpha, false);
        m_engine->rhfLaser().setParameters(tRawQ, tFiltQ, tRawAlpha, false);
        appendLog(QString::fromUtf8("H∞参数已更新（仅H∞生效）。"));
        return;
    }
    Q_UNUSED(uFiltAlpha);
}

void SmartTuningWidget::triggerAiTune(AlgorithmType algo)
{
    if (!m_aiTuningEnabled) {
        appendLog(QString::fromUtf8("<span style='color:#A65A00'>AI 调参当前已关闭，请先点击“开启调参”。</span>"));
        return;
    }
    appendLog(QString::fromUtf8("手动触发 AI 调参..."));
    if (algo == AlgoKalman) {
        if (m_akfTuner) m_akfTuner->maybeTune();
        return;
    }
    if (algo == AlgoOneEuro) {
        if (m_oefTuner) m_oefTuner->maybeTune();
        return;
    }
    if (algo == AlgoHinf) {
        if (m_rhfTuner) m_rhfTuner->maybeTune();
        return;
    }
}

void SmartTuningWidget::resetAlgorithmState(AlgorithmType algo)
{
    if (!m_engine) return;
    if (algo == AlgoKalman) {
        m_engine->akfUltrasonic().reset(false);
        m_engine->akfLaser().reset(false);
        syncControlsFromAlgorithm(AlgoKalman);
        appendLog(QString::fromUtf8("卡尔曼滤波状态已重置。"));
        return;
    }
    if (algo == AlgoOneEuro) {
        m_engine->oefUltrasonic().reset();
        m_engine->oefLaser().reset();
        syncControlsFromAlgorithm(AlgoOneEuro);
        appendLog(QString::fromUtf8("自适应低通滤波状态已重置。"));
        return;
    }
    if (algo == AlgoHinf) {
        m_engine->rhfUltrasonic().reset();
        m_engine->rhfLaser().reset();
        syncControlsFromAlgorithm(AlgoHinf);
        appendLog(QString::fromUtf8("H∞滤波状态已重置。"));
        return;
    }
}

void SmartTuningWidget::syncControlsFromAlgorithm(AlgorithmType algo)
{
    if (!m_engine) return;
    const int idx = static_cast<int>(algo);
    if (idx < 0 || idx >= m_paramPages.size()) return;
    const ParamControls &c = m_paramPages[idx];

    if (algo == AlgoKalman) {
        setSpinValueQuietly(c.spinRawQ, m_engine->akfUltrasonic().Q());
        setSpinValueQuietly(c.spinRawAlpha, m_engine->akfUltrasonic().Alpha());
        setSpinValueQuietly(c.spinTofRawQ, m_engine->akfLaser().Q());
        setSpinValueQuietly(c.spinTofRawAlpha, m_engine->akfLaser().Alpha());
        return;
    }
    if (algo == AlgoOneEuro) {
        setSpinValueQuietly(c.spinRawQ, m_engine->oefUltrasonic().minCutoff());
        setSpinValueQuietly(c.spinFiltQ, m_engine->oefUltrasonic().dCutoff());
        setSpinValueQuietly(c.spinRawAlpha, m_engine->oefUltrasonic().beta());
        setSpinValueQuietly(c.spinTofRawQ, m_engine->oefLaser().minCutoff());
        setSpinValueQuietly(c.spinTofFiltQ, m_engine->oefLaser().dCutoff());
        setSpinValueQuietly(c.spinTofRawAlpha, m_engine->oefLaser().beta());
        return;
    }
    if (algo == AlgoHinf) {
        setSpinValueQuietly(c.spinRawQ, m_engine->rhfUltrasonic().Q());
        setSpinValueQuietly(c.spinFiltQ, m_engine->rhfUltrasonic().R());
        setSpinValueQuietly(c.spinRawAlpha, m_engine->rhfUltrasonic().Gamma());
        setSpinValueQuietly(c.spinTofRawQ, m_engine->rhfLaser().Q());
        setSpinValueQuietly(c.spinTofFiltQ, m_engine->rhfLaser().R());
        setSpinValueQuietly(c.spinTofRawAlpha, m_engine->rhfLaser().Gamma());
        return;
    }
}

void SmartTuningWidget::setSpinValueQuietly(QDoubleSpinBox *spin, double value)
{
    if (!spin) return;
    const QSignalBlocker blocker(spin);
    spin->setValue(value);
}

void SmartTuningWidget::setAiTuningEnabled(bool enabled)
{
    const bool changed = (m_aiTuningEnabled != enabled);
    m_aiTuningEnabled = enabled;

    if (m_btnEnableTune) {
        m_btnEnableTune->setEnabled(!enabled);
    }
    if (m_btnDisableTune) {
        m_btnDisableTune->setEnabled(enabled);
    }

    if (changed) {
        if (enabled) {
            appendLog(QString::fromUtf8("<span style='color:green'>AI 调参已开启。</span>"));
        } else {
            appendLog(QString::fromUtf8("<span style='color:#A65A00'>AI 调参已关闭。</span>"));
        }
    }
}

void SmartTuningWidget::appendLog(const QString &htmlLine)
{
    if (!m_logEdit) return;
    m_logEdit->append(htmlLine);
}

