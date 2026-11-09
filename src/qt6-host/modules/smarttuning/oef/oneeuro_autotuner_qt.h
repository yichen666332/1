#ifndef ONEEURO_AUTOTUNER_QT_H
#define ONEEURO_AUTOTUNER_QT_H

#include <QObject>
#include <QDateTime>

#include "../../../core/filters/oneeurolikefilter.h"
#include "../../curveanalysis/oef/oefscorer_qt.h"
#include "../deepseekclient_qt.h"

class OneEuroAutoTunerQt : public QObject
{
    Q_OBJECT

public:
    struct Config
    {
        int   windowSize;
        int   minSamples;
        int   minIntervalMs;
        float minCutoffMin;
        float minCutoffMax;
        float dCutoffMin;
        float dCutoffMax;
        float betaMin;
        float betaMax;

        Config()
            : windowSize(256)
            , minSamples(150)
            , minIntervalMs(5000)
            , minCutoffMin(0.1f)
            , minCutoffMax(10.0f)
            , dCutoffMin(0.1f)
            , dCutoffMax(10.0f)
            , betaMin(0.0f)
            , betaMax(0.5f)
        {}
    };

    OneEuroAutoTunerQt(OneEuroLikeFilter &filter,
                       DeepSeekClientQt *client,
                       const Config &cfg = Config(),
                       QObject *parent = nullptr);

    void clear();
    void feed(double raw, double filtered);
    void maybeTune();

signals:
    void logMessage(const QString &html);
    void parametersApplied(float minCutoff, float dCutoff, float beta);

private:
    OneEuroLikeFilter &m_filter;
    OefScorerQt        m_scorer;
    Config             m_cfg;
    DeepSeekClientQt  *m_client = nullptr;
    qint64             m_lastTuneMs = 0;
    bool               m_inFlight = false;
};

#endif // ONEEURO_AUTOTUNER_QT_H

