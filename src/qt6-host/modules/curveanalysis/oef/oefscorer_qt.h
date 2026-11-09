#ifndef OEFSCORER_QT_H
#define OEFSCORER_QT_H

#include "../eval/evalwindow_qt.h"
#include <QString>
#include <limits>

struct OefScoreResultQt
{
    EvalStatQt stat;
    double score = 0.0;
    QString verdict;
};

class OefScorerQt
{
public:
    struct Config
    {
        double TargetSmoothRatio;
        double SmoothBand;
        double LagScale;
        double WSmooth;
        double WLag;
        bool   UseMadRatio;
        int    MinSamples;

        Config()
            : TargetSmoothRatio(0.70)
            , SmoothBand(0.25)
            , LagScale(1.0)
            , WSmooth(0.65)
            , WLag(0.35)
            , UseMadRatio(true)
            , MinSamples(150)
        {}
    };

    explicit OefScorerQt(int window = 256, const Config &cfg = Config())
        : m_win(window)
        , m_cfg(cfg)
    {
    }

    void clear() { m_win.clear(); }

    void feed(double raw, double filtered) { m_win.push(raw, filtered); }

    OefScoreResultQt evaluate() const
    {
        OefScoreResultQt res;
        res.stat = m_win.evaluate(m_cfg.UseMadRatio);
        if (res.stat.N < m_cfg.MinSamples) {
            res.score = std::numeric_limits<double>::quiet_NaN();
            res.verdict = QString::fromUtf8("样本不足");
            return res;
        }

        const double sr = res.stat.SmoothRatio;
        const double lag = res.stat.LagRms;

        const double srTerm = std::max(0.0, 1.0 - std::fabs(sr - m_cfg.TargetSmoothRatio) / m_cfg.SmoothBand);
        const double lagTerm = 1.0 / (1.0 + lag / std::max(1e-9, m_cfg.LagScale));

        res.score = 100.0 * (m_cfg.WSmooth * srTerm + m_cfg.WLag * lagTerm);

        if (sr < (m_cfg.TargetSmoothRatio - 0.15))
            res.verdict = QString::fromUtf8("过平滑（响应偏慢）");
        else if (sr > (m_cfg.TargetSmoothRatio + 0.15))
            res.verdict = QString::fromUtf8("不够平滑（抖动偏大）");
        else
            res.verdict = QString::fromUtf8("平衡良好");

        return res;
    }

private:
    EvalWindowQt m_win;
    Config m_cfg;
};

#endif // OEFSCORER_QT_H

