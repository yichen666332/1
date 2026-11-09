#include "robusthinf_autotuner_qt.h"

#include <QtMath>

RobustHInfAutoTunerQt::RobustHInfAutoTunerQt(RobustHInfinityFilter &filter,
                                             DeepSeekClientQt *client,
                                             const Config &cfg,
                                             QObject *parent)
    : QObject(parent)
    , m_filter(filter)
    , m_scorer(cfg.windowSize)
    , m_cfg(cfg)
    , m_client(client)
{
}

void RobustHInfAutoTunerQt::clear()
{
    m_scorer.clear();
}

void RobustHInfAutoTunerQt::feed(double raw, double filtered)
{
    m_scorer.feed(raw, filtered);
}

void RobustHInfAutoTunerQt::maybeTune()
{
    if (!m_client || m_inFlight)
        return;

    const auto res = m_scorer.evaluate();
    if (res.stat.N < m_cfg.minSamples)
        return;

    const qint64 now = QDateTime::currentMSecsSinceEpoch();
    if (m_lastTuneMs != 0 && (now - m_lastTuneMs) < m_cfg.minIntervalMs)
        return;

    m_lastTuneMs = now;
    m_inFlight = true;

    const QString systemPrompt = QString::fromUtf8(
        "You are an expert in robust H-infinity filters for 1D signals.\n"
        "You will receive statistics including SmoothRatio, LagRms and heavy-tail metrics, "
        "and current parameters Q, R, gamma.\n"
        "Return JSON only with keys: \"proposed_q\" (float), \"proposed_r\" (float), "
        "\"proposed_alpha\" (float gamma), and \"comment\" (string). No extra text."
    );

    const QString userPrompt = QString::fromUtf8(
        "Window statistics:\n"
        "N = %1\n"
        "SmoothRatio = %2\n"
        "LagRms = %3\n"
        "InnovAbsMean = %4\n"
        "Score = %5\n"
        "Please suggest adjusted Q, R, gamma within ranges: "
        "Q in [%6, %7], R in [%8, %9], gamma in [%10, %11].")
        .arg(res.stat.N)
        .arg(res.stat.SmoothRatio)
        .arg(res.stat.LagRms)
        .arg(res.stat.InnovAbsMean)
        .arg(res.score)
        .arg(m_cfg.qMin)
        .arg(m_cfg.qMax)
        .arg(m_cfg.rMin)
        .arg(m_cfg.rMax)
        .arg(m_cfg.gammaMin)
        .arg(m_cfg.gammaMax);

    emit logMessage(QString::fromUtf8("RobustHInfAutoTuner: 请求调参建议..."));

    m_client->getTuningAsync(systemPrompt, userPrompt,
                             [this](bool ok, const QJsonObject &json, const QString &err) {
        m_inFlight = false;
        if (!ok) {
            emit logMessage(QString::fromUtf8(
                                "<span style='color:red'>RobustHInfAutoTuner LLM 失败：%1</span>")
                            .arg(err.toHtmlEscaped()));
            return;
        }

        const float oldQ = m_filter.Q();
        const float oldR = m_filter.R();
        const float oldG = m_filter.Gamma();

        const double qVal = json.value(QStringLiteral("proposed_q")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const double rVal = json.value(QStringLiteral("proposed_r")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const double gammaVal = json.value(QStringLiteral("proposed_alpha")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const QString comment = json.value(QStringLiteral("comment")).toString();

        float newQ = oldQ;
        float newR = oldR;
        float newG = oldG;

        if (qVal == qVal) {
            newQ = static_cast<float>(qVal);
            newQ = std::max(m_cfg.qMin, std::min(m_cfg.qMax, newQ));
        }
        if (rVal == rVal) {
            newR = static_cast<float>(rVal);
            newR = std::max(m_cfg.rMin, std::min(m_cfg.rMax, newR));
        }
        if (gammaVal == gammaVal) {
            newG = static_cast<float>(gammaVal);
            newG = std::max(m_cfg.gammaMin, std::min(m_cfg.gammaMax, newG));
        }

        m_filter.setQ(newQ);
        m_filter.setR(newR);
        m_filter.setGamma(newG);
        emit parametersApplied(newQ, newR, newG);

        emit logMessage(QString::fromUtf8(
                            "<span style='color:green'>RobustHInfAutoTuner 更新参数：Q %1→%2, R %3→%4, gamma %5→%6。备注：%7</span>")
                        .arg(oldQ)
                        .arg(newQ)
                        .arg(oldR)
                        .arg(newR)
                        .arg(oldG)
                        .arg(newG)
                        .arg(comment.toHtmlEscaped()));
    });
}

