#ifndef KALMAN1D_H
#define KALMAN1D_H

#include <QtGlobal>

class Kalman1D
{
public:
    void reset(double x0 = 0.0, double p0 = 1.0)
    {
        m_x = x0;
        m_p = p0;
        m_inited = true;
    }

    void setParams(double q, double r)
    {
        m_q = q;
        m_r = r;
    }

    double update(double z)
    {
        if (!m_inited) {
            reset(z, 1.0);
            return m_x;
        }

        // Predict
        double x_pred = m_x;
        double p_pred = m_p + m_q;

        // Update
        double s = p_pred + m_r;
        double k = (s <= 0.0) ? 0.0 : (p_pred / s);
        m_x = x_pred + k * (z - x_pred);
        m_p = (1.0 - k) * p_pred;

        return m_x;
    }

private:
    double m_q = 0.01;
    double m_r = 1.0;
    double m_x = 0.0;
    double m_p = 1.0;
    bool m_inited = false;
};

#endif // KALMAN1D_H
