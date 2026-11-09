#include "oneeuro_autotuner_qt.h"

#include <QtMath>

OneEuroAutoTunerQt::OneEuroAutoTunerQt(OneEuroLikeFilter &filter,
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

void OneEuroAutoTunerQt::clear()
{
    m_scorer.clear();
}

void OneEuroAutoTunerQt::feed(double raw, double filtered)
{
    m_scorer.feed(raw, filtered);
}

void OneEuroAutoTunerQt::maybeTune()
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
        "You are an expert at tuning the One Euro filter (minCutoff, dCutoff, beta) for 1D noisy signals.\n"
        "You will receive window statistics and should respond with JSON only.\n"
        "JSON keys: \"proposed_q\" -> minCutoff (Hz), \"proposed_r\" -> dCutoff (Hz), "
        "\"proposed_alpha\" -> beta, plus \"comment\" (string). No extra text."
    );

    const QString userPrompt = QString::fromUtf8(
        "Window statistics:\n"
        "N = %1\n"
        "SmoothRatio = %2\n"
        "LagRms = %3\n"
        "InnovAbsMean = %4\n"
        "Score = %5\n"
        "Please suggest slightly adjusted minCutoff, dCutoff and beta within ranges:\n"
        "minCutoff in [%6, %7], dCutoff in [%8, %9], beta in [%10, %11].")
        .arg(res.stat.N)
        .arg(res.stat.SmoothRatio)
        .arg(res.stat.LagRms)
        .arg(res.stat.InnovAbsMean)
        .arg(res.score)
        .arg(m_cfg.minCutoffMin)
        .arg(m_cfg.minCutoffMax)
        .arg(m_cfg.dCutoffMin)
        .arg(m_cfg.dCutoffMax)
        .arg(m_cfg.betaMin)
        .arg(m_cfg.betaMax);

    emit logMessage(QString::fromUtf8("OneEuroAutoTuner: 请求调参建议..."));

    m_client->getTuningAsync(systemPrompt, userPrompt,
                             [this](bool ok, const QJsonObject &json, const QString &err) {
        m_inFlight = false;
        if (!ok) {
            emit logMessage(QString::fromUtf8(
                                "<span style='color:red'>OneEuroAutoTuner LLM 失败：%1</span>")
                            .arg(err.toHtmlEscaped()));
            return;
        }

        const double qVal = json.value(QStringLiteral("proposed_q")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const double rVal = json.value(QStringLiteral("proposed_r")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const double alphaVal = json.value(QStringLiteral("proposed_alpha")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const QString comment = json.value(QStringLiteral("comment")).toString();

        float minCutoff = m_cfg.minCutoffMin;
        float dCutoff = m_cfg.dCutoffMin;
        float beta = m_cfg.betaMin;

        if (qVal == qVal)
            minCutoff = std::max(m_cfg.minCutoffMin, std::min(m_cfg.minCutoffMax, static_cast<float>(qVal)));
        if (rVal == rVal)
            dCutoff = std::max(m_cfg.dCutoffMin, std::min(m_cfg.dCutoffMax, static_cast<float>(rVal)));
        if (alphaVal == alphaVal)
            beta = std::max(m_cfg.betaMin, std::min(m_cfg.betaMax, static_cast<float>(alphaVal)));

        m_filter.setParameters(minCutoff, dCutoff, beta, true);
        emit parametersApplied(minCutoff, dCutoff, beta);

        emit logMessage(QString::fromUtf8(
                            "<span style='color:green'>OneEuroAutoTuner 更新参数：minCutoff=%1, dCutoff=%2, beta=%3。备注：%4</span>")
                        .arg(minCutoff)
                        .arg(dCutoff)
                        .arg(beta)
                        .arg(comment.toHtmlEscaped()));
    });
}

