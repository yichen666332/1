#ifndef COMBINEDFILTERWIDGET_H
#define COMBINEDFILTERWIDGET_H

#include <QWidget>
#include <QTreeWidget>
#include <QSet>
#include <QLabel>

#include "../../../core/filters/adaptivekalmanfilter.h"
#include "../../../core/filters/robusthinfinityfilter.h"
#include "../../../core/filters/medianfilter.h"

struct FilterDataPacket;
class SimpleChartWidget;

class CombinedFilterWidget : public QWidget
{
    Q_OBJECT

public:
    explicit CombinedFilterWidget(QWidget *parent = nullptr);

public slots:
    void onDataUpdated(const FilterDataPacket &packet);
    void clearChart();

private slots:
    void onTreeItemChanged(QTreeWidgetItem *item, int column);

private:
    void setupUi();
    void setupTree();
    void setupChart();

    double valueForSeries(const QString &seriesId, double uMeas, double tMeas);

    // UI
    QTreeWidget *m_tree = nullptr;
    SimpleChartWidget *m_chart = nullptr;
    QLabel *m_hint = nullptr;
    QSet<QString> m_enabled;

    // 组合1：卡尔曼 + H∞（场景切换）
    AdaptiveKalmanFilter m_kh_kal_u;
    AdaptiveKalmanFilter m_kh_kal_t;
    RobustHInfinityFilter m_kh_hinf_u;
    RobustHInfinityFilter m_kh_hinf_t;
    float m_kh_switchZ = 3.0f; // |Z| > 阈值则用 H∞

    // 组合2：H∞ + 中值（串行：Median -> H∞）
    MedianFilter m_mh_med_u;
    MedianFilter m_mh_med_t;
    RobustHInfinityFilter m_mh_hinf_u;
    RobustHInfinityFilter m_mh_hinf_t;

    // 组合3：卡尔曼 + 中值（串行：Median -> Kalman）
    MedianFilter m_mk_med_u;
    MedianFilter m_mk_med_t;
    AdaptiveKalmanFilter m_mk_kal_u;
    AdaptiveKalmanFilter m_mk_kal_t;
};

#endif // COMBINEDFILTERWIDGET_H

