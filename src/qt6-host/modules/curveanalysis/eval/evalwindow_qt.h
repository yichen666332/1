#ifndef EVALWINDOW_QT_H
#define EVALWINDOW_QT_H

#include <deque>
#include <vector>
#include <cmath>
#include <algorithm>
#include <limits>

struct EvalStatQt
{
    int    N = 0;
    double InStd = 0.0;
    double OutStd = 0.0;
    double InMAD = 0.0;
    double OutMAD = 0.0;
    double SmoothRatio = 0.0;
    double LagRms = 0.0;
    int    LagSamples = 0;
    double InnovAbsMean = 0.0;
};

class EvalWindowQt
{
public:
    explicit EvalWindowQt(int capacity = 256)
        : m_cap(std::max(8, capacity))
    {
    }

    void clear()
    {
        m_q.clear();
        m_nIn = m_nOut = 0;
        m_meanIn = m_m2In = 0.0;
        m_meanOut = m_m2Out = 0.0;
        m_lagSumSq = 0.0;
    }

    void push(double xin, double xout)
    {
        m_q.emplace_back(xin, xout);

        // In
        ++m_nIn;
        double dIn = xin - m_meanIn;
        m_meanIn += dIn / m_nIn;
        m_m2In += dIn * (xin - m_meanIn);

        // Out
        ++m_nOut;
        double dOut = xout - m_meanOut;
        m_meanOut += dOut / m_nOut;
        m_m2Out += dOut * (xout - m_meanOut);

        // Lag RMS
        double e = xin - xout;
        m_lagSumSq += e * e;

        if (static_cast<int>(m_q.size()) > m_cap)
            recomputeAll();
    }

    int count() const { return static_cast<int>(m_q.size()); }

    EvalStatQt evaluate(bool useMadRatio = false, int lagSearchMax = 5) const
    {
        EvalStatQt stat;
        stat.N = static_cast<int>(m_q.size());
        if (stat.N == 0)
            return stat;

        stat.InStd = (m_nIn > 1) ? std::sqrt(m_m2In / static_cast<double>(m_nIn - 1)) : 0.0;
        stat.OutStd = (m_nOut > 1) ? std::sqrt(m_m2Out / static_cast<double>(m_nOut - 1)) : 0.0;

        // copy to vectors for MAD / cross-corr
        std::vector<double> xin;
        std::vector<double> xout;
        xin.reserve(stat.N);
        xout.reserve(stat.N);
        for (const auto &p : m_q) {
            xin.push_back(p.first);
            xout.push_back(p.second);
        }

        stat.InMAD = mad(xin);
        stat.OutMAD = mad(xout);

        if (useMadRatio) {
            stat.SmoothRatio = (stat.InMAD > 1e-9) ? (stat.OutMAD / stat.InMAD) : 0.0;
        } else {
            stat.SmoothRatio = (stat.InStd > 1e-9) ? (stat.OutStd / stat.InStd) : 0.0;
        }

        stat.LagRms = std::sqrt(m_lagSumSq / static_cast<double>(stat.N));
        stat.LagSamples = crossCorrLag(xin, xout, lagSearchMax);

        double sumAbs = 0.0;
        for (int i = 0; i < stat.N; ++i)
            sumAbs += std::fabs(xin[i] - xout[i]);
        stat.InnovAbsMean = sumAbs / static_cast<double>(stat.N);

        return stat;
    }

private:
    void recomputeAll()
    {
        while (static_cast<int>(m_q.size()) > m_cap)
            m_q.pop_front();

        m_nIn = m_nOut = 0;
        m_meanIn = m_m2In = 0.0;
        m_meanOut = m_m2Out = 0.0;
        m_lagSumSq = 0.0;

        for (const auto &p : m_q) {
            double xin = p.first;
            double xout = p.second;

            ++m_nIn;
            double dIn = xin - m_meanIn;
            m_meanIn += dIn / m_nIn;
            m_m2In += dIn * (xin - m_meanIn);

            ++m_nOut;
            double dOut = xout - m_meanOut;
            m_meanOut += dOut / m_nOut;
            m_m2Out += dOut * (xout - m_meanOut);

            double e = xin - xout;
            m_lagSumSq += e * e;
        }
    }

    static double mad(std::vector<double> a)
    {
        if (a.empty()) return 0.0;
        std::sort(a.begin(), a.end());
        const double median = a[a.size() / 2];
        for (double &v : a)
            v = std::fabs(v - median);
        std::sort(a.begin(), a.end());
        return a[a.size() / 2];
    }

    static int crossCorrLag(const std::vector<double> &x, const std::vector<double> &y, int maxLag)
    {
        if (x.empty() || y.empty()) return 0;
        const int n = static_cast<int>(std::min(x.size(), y.size()));
        double best = -std::numeric_limits<double>::infinity();
        int bestLag = 0;
        maxLag = std::min(maxLag, n - 1);

        for (int lag = -maxLag; lag <= maxLag; ++lag) {
            double s = 0.0;
            for (int i = 0; i < n; ++i) {
                const int j = i + lag;
                if (j < 0 || j >= n) continue;
                s += x[i] * y[j];
            }
            if (s > best) {
                best = s;
                bestLag = lag;
            }
        }
        return bestLag;
    }

    int m_cap;
    std::deque<std::pair<double, double>> m_q;

    int m_nIn = 0;
    int m_nOut = 0;
    double m_meanIn = 0.0;
    double m_m2In = 0.0;
    double m_meanOut = 0.0;
    double m_m2Out = 0.0;
    double m_lagSumSq = 0.0;
};

#endif // EVALWINDOW_QT_H

