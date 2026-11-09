#ifndef ONEEUROLIKEFILTER_H
#define ONEEUROLIKEFILTER_H

#include <algorithm>
#include <atomic>
#include <cmath>

class OneEuroLikeFilter
{
public:
    OneEuroLikeFilter(float dtSeconds,
                      float initialMinCutoff = 1.5f,
                      float initialDCutoff = 1.0f,
                      float initialBeta = 0.007f)
    {
        setDt(dtSeconds);
        m_minCutoff.store(std::max(initialMinCutoff, EPS_FC));
        m_dCutoff.store(std::max(initialDCutoff, EPS_FC));
        m_beta.store(std::max(initialBeta, 0.0f));
    }

    void setDt(float dtSeconds)
    {
        m_dt.store(std::max(dtSeconds, EPS_DT));
    }

    void reset()
    {
        m_initialized = false;
        m_xPrev = m_dxPrev = m_xHat = m_dxHat = 0.0f;
        m_updateCount = 0;
    }

    float update(float measurement)
    {
        const float dt = m_dt.load();
        if (!m_initialized) {
            m_xPrev = measurement;
            m_dxPrev = 0.0f;
            m_xHat = measurement;
            m_dxHat = 0.0f;
            m_initialized = true;
            m_updateCount = 0;
            m_lastInput.store(measurement);
            m_lastOutput.store(m_xHat);
            return m_xHat;
        }

        m_updateCount++;

        const float minCutoff = m_minCutoff.load();
        const float dCutoff = m_dCutoff.load();
        const float beta = m_beta.load();

        const float dxRaw = (measurement - m_xPrev) / dt;
        const float aD = alpha(dCutoff, dt);
        m_dxHat = m_dxPrev + aD * (dxRaw - m_dxPrev);

        const float cutoff = std::max(minCutoff + beta * std::fabs(m_dxHat), EPS_FC);
        const float aX = alpha(cutoff, dt);
        m_xHat = m_xHat + aX * (measurement - m_xHat);

        m_xPrev = measurement;
        m_dxPrev = m_dxHat;

        m_lastInput.store(measurement);
        m_lastOutput.store(m_xHat);
        return m_xHat;
    }

    void setMinCutoff(float hz) { m_minCutoff.store(std::max(hz, EPS_FC)); }
    void setDCutoff(float hz) { m_dCutoff.store(std::max(hz, EPS_FC)); }
    void setBeta(float beta) { m_beta.store(std::max(beta, 0.0f)); }
    void setParameters(float newQ, float newR, float newAlpha, bool resetState = false)
    {
        setMinCutoff(newQ);
        setDCutoff(newR);
        setBeta(newAlpha);
        if (resetState) reset();
    }

    float minCutoff() const { return m_minCutoff.load(); }
    float dCutoff() const { return m_dCutoff.load(); }
    float beta() const { return m_beta.load(); }

    float lastInput() const { return m_lastInput.load(); }
    float lastOutput() const { return m_lastOutput.load(); }

private:
    static constexpr float EPS_DT = 1e-6f;
    static constexpr float EPS_FC = 1e-3f;

    static float alpha(float cutoffHz, float dt)
    {
        const float tau = 1.0f / (2.0f * static_cast<float>(M_PI) * cutoffHz);
        return dt / (tau + dt);
    }

    std::atomic<float> m_minCutoff{1.5f};
    std::atomic<float> m_dCutoff{1.0f};
    std::atomic<float> m_beta{0.007f};
    std::atomic<float> m_dt{0.1f};

    bool m_initialized = false;
    float m_xPrev = 0.0f;
    float m_dxPrev = 0.0f;
    float m_xHat = 0.0f;
    float m_dxHat = 0.0f;
    int m_updateCount = 0;

    std::atomic<float> m_lastInput{0.0f};
    std::atomic<float> m_lastOutput{0.0f};
};

#endif // ONEEUROLIKEFILTER_H
