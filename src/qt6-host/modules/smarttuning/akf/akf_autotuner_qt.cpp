#include "akf_autotuner_qt.h"

#include <QtMath>

AkfAutoTunerQt::AkfAutoTunerQt(AdaptiveKalmanFilter &filter,
                               DeepSeekClientQt *client,
                               const Config &cfg,
                               QObject *parent)
    : QObject(parent)
    , m_filter(filter)
    , m_cfg(cfg)
    , m_client(client)
{
    m_ringZ.resize(m_cfg.windowSize);
    m_ringResidual.resize(m_cfg.windowSize);
    m_ringK.resize(m_cfg.windowSize);
}

void AkfAutoTunerQt::clear()
{
    m_writeIndex = 0;
    m_count = 0;
    m_hasBaseline = false;
}

void AkfAutoTunerQt::feed(double raw, double filtered)
{
    Q_UNUSED(raw);
    Q_UNUSED(filtered);

    // 直接从滤波器诊断量中取 Z / residual / K
    const double z = static_cast<double>(m_filter.lastInnovationZ());
    const double r = static_cast<double>(m_filter.lastResidual());
    const double k = static_cast<double>(m_filter.lastK());

    if (m_ringZ.isEmpty())
        return;

    m_ringZ[m_writeIndex] = z;
    m_ringResidual[m_writeIndex] = r;
    m_ringK[m_writeIndex] = k;

    m_writeIndex = (m_writeIndex + 1) % m_ringZ.size();
    if (m_count < m_ringZ.size())
        ++m_count;
}

AkfAutoTunerQt::EvalStatQtAkf AkfAutoTunerQt::evaluateWindow() const
{
    EvalStatQtAkf s;
    s.N = m_count;
    if (m_count <= 0)
        return s;

    const int n = m_count;
    const int cap = m_ringZ.size();
    const int start = (m_writeIndex - n + cap) % cap;

    double sumZ = 0.0, sumZ2 = 0.0;
    double sumR2 = 0.0;
    double sumK = 0.0;

    for (int i = 0; i < n; ++i) {
        const int idx = (start + i) % cap;
        const double z = m_ringZ[idx];
        const double r = m_ringResidual[idx];
        const double k = m_ringK[idx];

        sumZ += z;
        sumZ2 += z * z;
        sumR2 += r * r;
        sumK += k;
    }

    const double meanZ = sumZ / n;
    s.zMean = meanZ;
    s.zVar = (sumZ2 / n) - meanZ * meanZ;
    if (s.zVar < 0.0) s.zVar = 0.0;

    s.residualRms = std::sqrt(sumR2 / n);
    s.meanK = sumK / n;
    return s;
}

double AkfAutoTunerQt::computeScore(const EvalStatQtAkf &stat, float q, float alpha) const
{
    if (stat.N <= 0)
        return 0.0;

    const double zVarErr = std::fabs(stat.zVar - 1.0);
    const double kErr = std::fabs(stat.meanK - 0.5);

    double score = 100.0;
    score -= 30.0 * std::min(1.5, zVarErr);   // 越接近 1 越高分
    score -= 20.0 * std::min(1.0, kErr);      // K 越接近 0.5 越高分
    score -= 25.0 * std::min(1.0, stat.residualRms); // 剩余 RMS 越小越好

    // 对极端 Q/Alpha 做轻微惩罚，避免发散
    if (q > 0.1f)
        score -= 5.0;
    if (alpha < 0.85f)
        score -= 5.0;

    if (score < 0.0) score = 0.0;
    if (score > 100.0) score = 100.0;
    return score;
}

void AkfAutoTunerQt::maybeTune()
{
    if (!m_client || m_inFlight)
        return;

    const EvalStatQtAkf stat = evaluateWindow();
    if (stat.N < m_cfg.minSamples)
        return;

    const float curQ = m_filter.Q();
    const float curAlpha = m_filter.Alpha();
    const double curScore = computeScore(stat, curQ, curAlpha);

    if (!m_hasBaseline) {
        m_baseQ = curQ;
        m_baseAlpha = curAlpha;
        m_baseScore = curScore;
        m_baseRms = stat.residualRms;
        m_hasBaseline = true;
        emit logMessage(QString::fromUtf8(
                            "AKF AutoTuner: 建立基线，Q=%1, Alpha=%2, score=%3")
                        .arg(curQ).arg(curAlpha).arg(curScore, 0, 'f', 1));
        return;
    }

    const qint64 now = QDateTime::currentMSecsSinceEpoch();
    if (m_lastTuneMs != 0 && (now - m_lastTuneMs) < m_cfg.minIntervalMs)
        return;

    // 若当前分数已经接近目标且 RMS 没明显变差，则不再调参
    if (curScore >= m_cfg.targetScore - 1.0 &&
        stat.residualRms <= m_baseRms * (1.0 + m_cfg.maxRmsWorsen)) {
        return;
    }

    m_lastTuneMs = now;
    m_inFlight = true;

    const QString systemPrompt = QString::fromUtf8(
        "You are an adaptive Kalman filter tuning assistant.\n"
        "You receive statistics of the whitened residual z (mean, variance), Kalman gain K, and residual RMS.\n"
        "Your goal is to make z approximately N(0,1), keep K around 0.5, and keep residual RMS small.\n"
        "Only output pure JSON: {\"proposed_q\": float, \"proposed_alpha\": float, \"comment\": string}."
    );

    const QString userPrompt = QString::fromUtf8(
        "Current stats over window N=%1:\n"
        "zMean=%2, zVar=%3, meanK=%4, residualRms=%5.\n"
        "Current params: Q=%6, Alpha=%7.\n"
        "Baseline score was %8, current score is %9.\n"
        "Suggest small adjustments to Q and Alpha within ranges:\n"
        "Q in [%10, %11], Alpha in [%12, %13].")
        .arg(stat.N)
        .arg(stat.zMean, 0, 'f', 3)
        .arg(stat.zVar, 0, 'f', 3)
        .arg(stat.meanK, 0, 'f', 3)
        .arg(stat.residualRms, 0, 'f', 3)
        .arg(curQ)
        .arg(curAlpha)
        .arg(m_baseScore, 0, 'f', 1)
        .arg(curScore, 0, 'f', 1)
        .arg(m_cfg.qMin)
        .arg(m_cfg.qMax)
        .arg(m_cfg.alphaMin)
        .arg(m_cfg.alphaMax);

    emit logMessage(QString::fromUtf8("AKF AutoTuner: 请求调参建议..."));

    m_client->getTuningAsync(systemPrompt, userPrompt,
                             [this, curQ, curAlpha, stat, curScore](bool ok, const QJsonObject &json, const QString &err) {
        m_inFlight = false;
        if (!ok) {
            emit logMessage(QString::fromUtf8(
                                "<span style='color:red'>AKF AutoTuner LLM 失败：%1</span>")
                            .arg(err.toHtmlEscaped()));
            return;
        }

        double qVal = json.value(QStringLiteral("proposed_q")).toDouble(std::numeric_limits<double>::quiet_NaN());
        double alphaVal = json.value(QStringLiteral("proposed_alpha")).toDouble(std::numeric_limits<double>::quiet_NaN());
        const QString comment = json.value(QStringLiteral("comment")).toString();

        if (!(qVal == qVal))
            qVal = curQ;
        if (!(alphaVal == alphaVal))
            alphaVal = curAlpha;

        float qNew = static_cast<float>(qVal);
        float aNew = static_cast<float>(alphaVal);

        qNew = std::max(m_cfg.qMin, std::min(m_cfg.qMax, qNew));
        aNew = std::max(m_cfg.alphaMin, std::min(m_cfg.alphaMax, aNew));

        // 步长限制（避免一次改太多）
        const float maxStep = 0.3f;
        auto clampStep = [maxStep](float oldV, float newV) {
            if (oldV == 0.0f) return newV;
            const float lo = oldV * (1.0f - maxStep);
            const float hi = oldV * (1.0f + maxStep);
            if (newV < lo) return lo;
            if (newV > hi) return hi;
            return newV;
        };
        qNew = clampStep(curQ, qNew);
        aNew = clampStep(curAlpha, aNew);

        m_filter.applyHyperParams(qNew, aNew, true);
        clear(); // 重建窗口
        emit parametersApplied(qNew, aNew);

        emit logMessage(QString::fromUtf8(
                            "<span style='color:green'>AKF AutoTuner 更新参数：Q %1→%2, Alpha %3→%4。备注：%5</span>")
                        .arg(curQ)
                        .arg(qNew)
                        .arg(curAlpha)
                        .arg(aNew)
                        .arg(comment.toHtmlEscaped()));
    });
}

