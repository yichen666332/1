#ifndef ADAPTIVEKALMANFILTER_H
#define ADAPTIVEKALMANFILTER_H

#include <algorithm>
#include <atomic>
#include <cmath>

class AdaptiveKalmanFilter
{
public:
    AdaptiveKalmanFilter(float initialQ = 0.01f, float initialR = 1.0f, float initialAlpha = 0.95f)
    {
        m_q.store(std::max(initialQ, 1e-12f));
        m_r.store(std::max(initialR, 1e-9f));
        m_alpha.store(clamp(initialAlpha, 0.70f, 0.995f));
    }

    float Q() const { return m_q.load(); }
    float Alpha() const { return m_alpha.load(); }
    float RCurrent() const { return m_r.load(); }

    int updateCount() const { return m_updateCount; }
    float lastResidual() const { return m_lastResidual; }
    float lastInnovationZ() const { return m_lastZ; }
    float lastK() const { return m_lastK; }

    void setQ(float newQ) { if (!m_fixedQ) m_q.store(std::max(newQ, 1e-12f)); }
    void setAlpha(float newAlpha) { if (!m_fixedAlpha) m_alpha.store(clamp(newAlpha, 0.70f, 0.995f)); }
    void setParameters(float newQ, float newAlpha)
    {
        setQ(newQ);
        setAlpha(newAlpha);
    }

    // 应用一组新的超参数（Q, Alpha），可选择是否重置内部诊断统计
    void applyHyperParams(float newQ, float newAlpha, bool clearDiagnostics = true)
    {
        setParameters(newQ, newAlpha);
        if (clearDiagnostics) {
            m_initialized = false;
            m_pEst = 0.0f;
            m_updateCount = 0;
            m_lastResidual = 0.0f;
            m_lastZ = 0.0f;
            m_lastK = 0.0f;
        }
    }

    void freezeFixedParamsOnly(bool fixQ = true, bool fixAlpha = true)
    {
        m_fixedQ = fixQ;
        m_fixedAlpha = fixAlpha;
    }

    void setAdaptiveRUpdateEnabled(bool enabled) { m_adaptiveRUpdateEnabled = enabled; }
    /// 创新门限（|z| 超过则视为野值：用当前测量重初始化状态，避免滤波值长期卡在错误平台）
    void setInnovationGateSigma(float sigma) { m_innovationGateSigma = std::max(0.f, sigma); }
    float innovationGateSigma() const { return m_innovationGateSigma; }

    void setRBounds(bool hasMin, float rMin, bool hasMax, float rMax)
    {
        m_hasRMin = hasMin;
        m_hasRMax = hasMax;
        m_rMin = rMin;
        m_rMax = rMax;
    }

    float update(float measurement)
    {
        if (!m_initialized) {
            m_xEst = measurement;
            m_pEst = 1.0f;
            m_initialized = true;
            m_updateCount = 0;
            return m_xEst;
        }

        m_updateCount++;

        const float qNow = m_q.load();
        float rNow = m_r.load();
        const float aNow = m_alpha.load();

        const float xPred = m_xEst; //预测阶段
        const float pPred = m_pEst + qNow; //协方差预测

        float S = pPred + rNow;
        if (S < 1e-12f) S = 1e-12f;
        const float K = pPred / S; //卡尔曼增益计算

        const float residual = measurement - xPred;
        const float sqrtS = static_cast<float>(std::sqrt(static_cast<double>(S)));
        const float invSqrt = sqrtS > 1e-12f ? (1.0f / sqrtS) : 0.f;
        const float zAbs = invSqrt > 0.f ? std::fabs(residual) * invSqrt : 1e9f;

        if (m_innovationGateSigma > 0.f && zAbs > m_innovationGateSigma) {
            m_xEst = measurement;
            m_pEst = 1.0f;
            m_lastResidual = residual;
            m_lastZ = residual * invSqrt;
            m_lastK = 0.0f;
            return m_xEst;
        }

        m_xEst = xPred + K * residual; //状态更新
        m_pEst = (1.0f - K) * pPred; //协方差更新公式

        // 记录诊断量：残差、归一化残差 Z、Kalman 增益 K
        m_lastResidual = residual;
        m_lastZ = residual * invSqrt;
        m_lastK = K;

        if (m_adaptiveRUpdateEnabled && m_updateCount >= 5) {
            const float residualSq = residual * residual;
            rNow = aNow * rNow + (1.0f - aNow) * residualSq; //自适应R调整
            rNow = std::max(rNow, 1e-9f);
            if (m_hasRMin) rNow = std::max(rNow, m_rMin);
            if (m_hasRMax) rNow = std::min(rNow, m_rMax);
            m_r.store(rNow);
        }

        return m_xEst;
    }

    void reset(bool resetR = false, float rInit = 0.0f)
    {
        m_initialized = false;
        if (resetR) {
            m_r.store(std::max(rInit, 1e-9f));
        }
    }

private:
    static float clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    float m_xEst = 0.0f;
    float m_pEst = 0.0f;
    bool m_initialized = false;
    int m_updateCount = 0;

    std::atomic<float> m_q{0.01f};
    std::atomic<float> m_r{1.0f};
    std::atomic<float> m_alpha{0.95f};

    bool m_adaptiveRUpdateEnabled = true;
    bool m_hasRMin = false;
    bool m_hasRMax = false;
    float m_rMin = 0.0f;
    float m_rMax = 0.0f;

    bool m_fixedQ = false;
    bool m_fixedAlpha = false;

    float m_innovationGateSigma = 0.f;

    // 诊断量
    float m_lastResidual = 0.0f;
    float m_lastZ = 0.0f;
    float m_lastK = 0.0f;
};

#endif // ADAPTIVEKALMANFILTER_H
