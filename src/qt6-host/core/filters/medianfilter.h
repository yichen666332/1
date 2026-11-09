#ifndef MEDIANFILTER_H
#define MEDIANFILTER_H

#include <QVector>
#include <algorithm>

class MedianFilter
{
public:
    explicit MedianFilter(int windowSize = 5)
        : m_windowSize(std::max(1, windowSize))
    {
        m_buffer.reserve(m_windowSize);
    }

    void reset()
    {
        m_buffer.clear();
    }

    void setWindowSize(int windowSize)
    {
        m_windowSize = std::max(1, windowSize);
        m_buffer.clear();
        m_buffer.reserve(m_windowSize);
    }

    double update(double v)
    {
        if (m_buffer.size() >= m_windowSize) {
            m_buffer.removeFirst();
        }
        m_buffer.append(v);

        QVector<double> tmp = m_buffer;
        std::sort(tmp.begin(), tmp.end());
        int n = tmp.size();
        if (n == 0) return v;
        if (n % 2 == 1) return tmp[n / 2];
        return 0.5 * (tmp[n / 2 - 1] + tmp[n / 2]);
    }

private:
    int m_windowSize;
    QVector<double> m_buffer;
};

#endif // MEDIANFILTER_H
