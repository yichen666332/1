#ifndef SMARTTUNINGWIDGET_H
#define SMARTTUNINGWIDGET_H

#include <QWidget>
#include <QTabWidget>
#include <QDoubleSpinBox>
#include <QComboBox>
#include <QPushButton>
#include <QTextEdit>
#include <QLabel>
#include <QVector>

#include "deepseekclient_qt.h"

class SerialDataEngine;
class AkfAutoTunerQt;
class OneEuroAutoTunerQt;
class RobustHInfAutoTunerQt;

class SmartTuningWidget : public QWidget
{
    Q_OBJECT

public:
    explicit SmartTuningWidget(QWidget *parent = nullptr);
    ~SmartTuningWidget();

    void setSerialEngine(SerialDataEngine *engine);

public slots:
    void onDataUpdated(const struct FilterDataPacket &packet);

private:
    enum AlgorithmType {
        AlgoKalman = 0,
        AlgoOneEuro = 1,
        AlgoHinf = 2
    };

    struct ParamControls {
        QDoubleSpinBox *spinRawQ = nullptr;
        QDoubleSpinBox *spinRawAlpha = nullptr;
        QDoubleSpinBox *spinFiltQ = nullptr;
        QDoubleSpinBox *spinFiltAlpha = nullptr;
        QDoubleSpinBox *spinTofRawQ = nullptr;
        QDoubleSpinBox *spinTofRawAlpha = nullptr;
        QDoubleSpinBox *spinTofFiltQ = nullptr;
        QDoubleSpinBox *spinTofFiltAlpha = nullptr;
        QPushButton *btnReset = nullptr;
        QPushButton *btnAITune = nullptr;
    };

    void setupUi();
    QWidget *createParameterPage(AlgorithmType algo, const QString &tabTitle);
    void bindParameterControls(AlgorithmType algo, const ParamControls &controls);
    void applyManualParameters(AlgorithmType algo);
    void triggerAiTune(AlgorithmType algo);
    void resetAlgorithmState(AlgorithmType algo);
    void syncControlsFromAlgorithm(AlgorithmType algo);
    void setSpinValueQuietly(QDoubleSpinBox *spin, double value);
    void setAiTuningEnabled(bool enabled);

    void appendLog(const QString &htmlLine);

    QTabWidget *m_tabWidget = nullptr;
    QVector<ParamControls> m_paramPages;

    DeepSeekClientQt *m_deepseekClient;
    QTextEdit *m_logEdit;
    QPushButton *m_btnPing;
    QPushButton *m_btnEnableTune = nullptr;
    QPushButton *m_btnDisableTune = nullptr;
    bool m_hasDeepSeek;
    bool m_aiTuningEnabled = true;

    SerialDataEngine *m_engine = nullptr;
    AkfAutoTunerQt   *m_akfTuner = nullptr;
    OneEuroAutoTunerQt *m_oefTuner = nullptr;
    RobustHInfAutoTunerQt *m_rhfTuner = nullptr;
};

#endif // SMARTTUNINGWIDGET_H
