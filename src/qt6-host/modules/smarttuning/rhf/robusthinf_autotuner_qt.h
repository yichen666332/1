#ifndef ROBUSTHINF_AUTOTUNER_QT_H
#define ROBUSTHINF_AUTOTUNER_QT_H

#include <QObject>
#include <QDateTime>

#include "../../../core/filters/robusthinfinityfilter.h"
#include "../../curveanalysis/rhf/rhfscorer_qt.h"
#include "../deepseekclient_qt.h"

class RobustHInfAutoTunerQt : public QObject
{
    Q_OBJECT

public:
    struct Config
    {
        int   windowSize;
        int   minSamples;
        int   minIntervalMs;
        float qMin;
        float qMax;
        float rMin;
        float rMax;
        float gammaMin;
        float gammaMax;

        Config()
            : windowSize(256)
            , minSamples(150)
            , minIntervalMs(5000)
            , qMin(1e-6f)
            , qMax(1.0f)
            , rMin(1e-6f)
            , rMax(10.0f)
            , gammaMin(0.01f)
            , gammaMax(0.9f)
        {}
    };

    RobustHInfAutoTunerQt(RobustHInfinityFilter &filter,
                          DeepSeekClientQt *client,
                          const Config &cfg = Config(),
                          QObject *parent = nullptr);

    void clear();
    void feed(double raw, double filtered);
    void maybeTune();

signals:
    void logMessage(const QString &html);
    void parametersApplied(float q, float r, float gamma);

private:
    RobustHInfinityFilter &m_filter;
    RhfScorerQt            m_scorer;
    Config                 m_cfg;
    DeepSeekClientQt      *m_client = nullptr;
    qint64                 m_lastTuneMs = 0;
    bool                   m_inFlight = false;
};

#endif // ROBUSTHINF_AUTOTUNER_QT_H

