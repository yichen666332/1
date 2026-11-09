#ifndef ROBUSTHINFINITYFILTER_H
#define ROBUSTHINFINITYFILTER_H

#include <algorithm>
#include <atomic>
#include <cmath>

class RobustHInfinityFilter
{
public:
    RobustHInfinityFilter(float initialQ = 0.01f, float initialR = 1.0f, float gamma = 0.1f, float huberC = 2.5f)
    {
        m_q.store(std::max(initialQ, EPS_Q));
        m_r.store(std::max(initialR, EPS_R));
        m_gamma.store(clamp(gamma, 1e-4f, 0.99f));
        m_huberC = std::max(1.0f, huberC);
    }

    float update(float measurement)
    {
        const float q = m_q.load();
        const float r = m_r.load();
        const float gamma = m_gamma.load();

        m_lastInput = measurement;

        if (!m_initialized) {
            m_xEst = measurement;
            m_pEst = std::max(1.0f, r);
            m_initialized = true;
            m_updateCount = 0;
            return m_xEst;
        }

        m_updateCount++;

        float pPred = (1.0f + gamma) * (m_pEst + q);
        pPred = std::max(pPred, EPS_P);

        float s = pPred + r;
        s = std::max(s, EPS_R + EPS_P);
        const float k = pPred / s;

        float residual = measurement - m_xEst;
        const float sigma = std::sqrt(s);
        const float thr = m_huberC * sigma;
        if (std::fabs(residual) > thr) residual = (residual > 0.0f ? thr : -thr);

        m_xEst = m_xEst + k * residual;

        const float oneMinusK = 1.0f - k;
        m_pEst = oneMinusK * oneMinusK * pPred + k * k * r;
        m_pEst = std::max(m_pEst, EPS_P);

        m_lastOutput = m_xEst;
        return m_xEst;
    }

    float Q() const { return m_q.load(); }
    float R() const { return m_r.load(); }
    float Gamma() const { return m_gamma.load(); }

    void reset()
    {
        m_initialized = false;
        m_xEst = 0.0f;
        m_pEst = 0.0f;
        m_updateCount = 0;
    }

    void setQ(float newQ) { m_q.store(std::max(newQ, EPS_Q)); }
    void setR(float newR) { m_r.store(std::max(newR, EPS_R)); }
    void setGamma(float newGamma) { m_gamma.store(clamp(newGamma, 1e-4f, 0.99f)); }
    void setHuberC(float huberC) { m_huberC = std::max(1.0f, huberC); }

    void setParameters(float newQ, float newR, float newAlpha, bool resetState = false)
    {
        setQ(newQ);
        setR(newR);
        setGamma(newAlpha);
        if (resetState) reset();
    }

    float lastInput() const { return m_lastInput; }
    float lastOutput() const { return m_lastOutput; }

private:
    static constexpr float EPS_R = 1e-6f;
    static constexpr float EPS_Q = 1e-9f;
    static constexpr float EPS_P = 1e-9f;

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
    std::atomic<float> m_gamma{0.1f};
    float m_huberC = 2.5f;

    float m_lastInput = 0.0f;
    float m_lastOutput = 0.0f;
};

#endif // ROBUSTHINFINITYFILTER_H
