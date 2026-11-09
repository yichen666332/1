#ifndef HINFFILTER1D_H
#define HINFFILTER1D_H

class HinfFilter1D
{
public:
    void reset(double x0 = 0.0, double p0 = 1.0)
    {
        m_x = x0;
        m_p = p0;
        m_inited = true;
    }

    // gamma > 1 越大越“保守”（对测量值更不信任）
    void setParams(double q, double r, double gamma)
    {
        m_q = q;
        m_r = r;
        m_gamma = (gamma <= 1.0) ? 1.0 : gamma;
    }

    double update(double z)
    {
        if (!m_inited) {
            reset(z, 1.0);
            return m_x;
        }

        double x_pred = m_x;
        double p_pred = m_p + m_q;

        // 这里用“放大测量噪声”的方式实现 H∞ 的保守性（gamma 越大，增益越小）
        double r_eff = m_r * m_gamma * m_gamma;
        double s = p_pred + r_eff;
        double k = (s <= 0.0) ? 0.0 : (p_pred / s);

        m_x = x_pred + k * (z - x_pred);
        m_p = (1.0 - k) * p_pred;
        return m_x;
    }

private:
    double m_q = 0.01;
    double m_r = 1.0;
    double m_gamma = 3.0;
    double m_x = 0.0;
    double m_p = 1.0;
    bool m_inited = false;
};

#endif // HINFFILTER1D_H
