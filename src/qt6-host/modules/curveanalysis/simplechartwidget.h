#ifndef SIMPLECHARTWIDGET_H
#define SIMPLECHARTWIDGET_H

#include <QWidget>
#include <QVector>
#include <QPointF>
#include <QMap>
#include <QString>
#include <QColor>

struct SeriesData {
    QString displayName;
    QColor color;
    QVector<QPointF> points;
};

class SimpleChartWidget : public QWidget
{
    Q_OBJECT

public:
    explicit SimpleChartWidget(QWidget *parent = nullptr);
    /** 确保存在该系列并设置显示名（若为新系列则分配颜色） */
    void ensureSeries(const QString &seriesId, const QString &displayName);
    /** 向指定系列追加一个点（时间由内部计时） */
    void appendPoint(const QString &seriesId, double value);
    /** 兼容：向默认系列 "default" 追加点 */
    void appendPoint(double value);
    /** 清除指定系列的数据 */
    void clearSeries(const QString &seriesId);
    void clear();
    void setWindowSeconds(double seconds);

protected:
    void paintEvent(QPaintEvent *event) override;

private:
    static const QVector<QColor> &palette();
    void trimToWindow(const QString &seriesId);
    double currentTimeSec();

    QMap<QString, SeriesData> m_series;
    QStringList m_seriesOrder;
    double m_windowSeconds = 20.0;
    double m_lastT = 0.0;
    bool m_timeInited = false;
};

#endif // SIMPLECHARTWIDGET_H
