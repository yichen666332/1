#ifndef AKF_AUTOTUNER_QT_H
#define AKF_AUTOTUNER_QT_H

#include <QObject>
#include <QDateTime>

#include "../../../core/filters/adaptivekalmanfilter.h"
#include "../deepseekclient_qt.h"

class AkfAutoTunerQt : public QObject
{
    Q_OBJECT

public:
    struct Config
    {
        int   windowSize;      // 采样窗口容量
        int   minSamples;      // 建立基线/评估所需最少样本
        int   minIntervalMs;   // 两次调参之间的冷却时间
        float qMin;
        float qMax;
        float alphaMin;
        float alphaMax;
        float targetScore;     // 目标分数
        float scoreTolerance;  // 容忍窗口
        float minScoreGain;    // 严格通过的最小增益
        float maxRmsWorsen;    // 允许 RMS 恶化比例

        Config()
            : windowSize(256)
            , minSamples(180)
            , minIntervalMs(6000)
            , qMin(1e-6f)
            , qMax(0.5f)
            , alphaMin(0.80f)
            , alphaMax(0.99f)
            , targetScore(85.0f)
            , scoreTolerance(5.0f)
            , minScoreGain(1.5f)
            , maxRmsWorsen(0.12f)
        {}
    };

    struct EvalStatQtAkf
    {
        int    N = 0;
        double zMean = 0.0;
        double zVar = 0.0;
        double meanK = 0.0;
        double residualRms = 0.0;
    };

    AkfAutoTunerQt(AdaptiveKalmanFilter &filter,
                   DeepSeekClientQt *client,
                   const Config &cfg = Config(),
                   QObject *parent = nullptr);

    void clear();
    void feed(double raw, double filtered);
    void maybeTune();

signals:
    void logMessage(const QString &html);
    void parametersApplied(float q, float alpha);

private:
    AdaptiveKalmanFilter &m_filter;
    Config                m_cfg;
    DeepSeekClientQt     *m_client = nullptr;

    // 窗口：存 Z / residual / K
    QVector<double> m_ringZ;
    QVector<double> m_ringResidual;
    QVector<double> m_ringK;
    int             m_writeIndex = 0;
    int             m_count = 0;

    // 基线
    bool   m_hasBaseline = false;
    float  m_baseQ = 0.0f;
    float  m_baseAlpha = 0.0f;
    double m_baseScore = 0.0;
    double m_baseRms = 0.0;

    qint64 m_lastTuneMs = 0;
    bool   m_inFlight = false;

    EvalStatQtAkf evaluateWindow() const;
    double computeScore(const EvalStatQtAkf &stat, float q, float alpha) const;
};

#endif // AKF_AUTOTUNER_QT_H

