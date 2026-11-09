#ifndef RHFSCORER_QT_H
#define RHFSCORER_QT_H

#include "../eval/evalwindow_qt.h"
#include <QString>
#include <limits>

struct RhfScoreResultQt
{
    EvalStatQt stat;
    double score = 0.0;
    QString verdict;
};

class RhfScorerQt
{
public:
    struct Config
    {
        double TargetSmoothRatio;
        double SmoothBand;
        double LagScale;
        double WSmooth;
        double WLag;
        double WHeavyTail;
        bool   UseMadRatio;
        int    MinSamples;
        double InnovVsMadRef;

        Config()
            : TargetSmoothRatio(0.65)
            , SmoothBand(0.30)
            , LagScale(1.0)
            , WSmooth(0.60)
            , WLag(0.30)
            , WHeavyTail(0.10)
            , UseMadRatio(true)
            , MinSamples(150)
            , InnovVsMadRef(3.0)
        {}
    };

    explicit RhfScorerQt(int window = 256, const Config &cfg = Config())
        : m_win(window)
        , m_cfg(cfg)
    {
    }

    void clear() { m_win.clear(); }

    void feed(double raw, double filtered) { m_win.push(raw, filtered); }

    RhfScoreResultQt evaluate() const
    {
        RhfScoreResultQt res;
        res.stat = m_win.evaluate(m_cfg.UseMadRatio, 6);
        if (res.stat.N < m_cfg.MinSamples) {
            res.score = std::numeric_limits<double>::quiet_NaN();
            res.verdict = QString::fromUtf8("样本不足");
            return res;
        }

        const double sr = res.stat.SmoothRatio;
        const double lag = res.stat.LagRms;

        const double srTerm = std::max(0.0, 1.0 - std::fabs(sr - m_cfg.TargetSmoothRatio) / m_cfg.SmoothBand);
        const double lagTerm = 1.0 / (1.0 + lag / std::max(1e-9, m_cfg.LagScale));

        const double heavy = (res.stat.InMAD > 1e-9) ? (res.stat.InnovAbsMean / res.stat.InMAD) : 0.0;
        const double heavyTerm = 1.0 / (1.0 + heavy / std::max(1e-9, m_cfg.InnovVsMadRef));

        res.score = 100.0 * (m_cfg.WSmooth * srTerm + m_cfg.WLag * lagTerm + m_cfg.WHeavyTail * heavyTerm);

        if (sr < (m_cfg.TargetSmoothRatio - 0.20))
            res.verdict = QString::fromUtf8("过平滑/可能滞后");
        else if (sr > (m_cfg.TargetSmoothRatio + 0.25))
            res.verdict = QString::fromUtf8("不够平滑/重噪");
        else if (heavy > 3.5)
            res.verdict = QString::fromUtf8("重尾明显");
        else
            res.verdict = QString::fromUtf8("平衡良好");

        return res;
    }

private:
    EvalWindowQt m_win;
    Config m_cfg;
};

#endif // RHFSCORER_QT_H

