#ifndef CURVEANALYSISWIDGET_H
#define CURVEANALYSISWIDGET_H

#include <QWidget>
#include <QTreeWidget>
#include <QLabel>
#include <QSet>
#include <QTabWidget>
#include <QHash>

#include "eval/evalwindow_qt.h"
#include "oef/oefscorer_qt.h"
#include "rhf/rhfscorer_qt.h"

struct FilterDataPacket;

class SimpleChartWidget;
class CombinedFilterWidget;

class CurveAnalysisWidget : public QWidget
{
    Q_OBJECT

public:
    explicit CurveAnalysisWidget(QWidget *parent = nullptr);
    ~CurveAnalysisWidget();

public slots:
    void onDataUpdated(const FilterDataPacket &packet);
    void clearChart();

private slots:
    void onTreeItemChanged(QTreeWidgetItem *item, int column);

private:
    void setupUi();
    void setupDataTree();
    void setupChartArea();
    void rebuildEnabledSeries();
    double valueForSeries(const QString &seriesId, const FilterDataPacket &packet) const;
    void updateScoreLabel();

    QTreeWidget *m_dataTree;
    QTabWidget *m_tabs = nullptr;
    QWidget *m_chartWidget;
    SimpleChartWidget *m_chart;
    CombinedFilterWidget *m_combined = nullptr;

    QHash<QString, OefScorerQt> m_oefScorers;
    QHash<QString, RhfScorerQt> m_rhfScorers;
    QHash<QString, QString> m_seriesDisplayNames;
    QLabel *m_scoreLabel;

    // 当前启用的曲线系列（seriesId）
    QSet<QString> m_enabledSeries;
};

#endif // CURVEANALYSISWIDGET_H
