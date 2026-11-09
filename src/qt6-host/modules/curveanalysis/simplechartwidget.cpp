#include "simplechartwidget.h"
#include <QPainter>
#include <QPaintEvent>
#include <QtMath>
#include <QElapsedTimer>
#include <QFontMetrics>

static QElapsedTimer &chartTimer()
{
    static QElapsedTimer t;
    return t;
}

SimpleChartWidget::SimpleChartWidget(QWidget *parent)
    : QWidget(parent)
{
    setMinimumHeight(300);
    setStyleSheet("background-color: white; border: 1px solid #E0E0E0; border-radius: 4px;");
    ensureSeries(QStringLiteral("default"), QStringLiteral("默认"));
}

void SimpleChartWidget::setWindowSeconds(double seconds)
{
    m_windowSeconds = qBound(1.0, seconds, 120.0);
}

const QVector<QColor> &SimpleChartWidget::palette()
{
    static const QVector<QColor> colors = {
        QColor(220, 53, 69),   // 红
        QColor(33, 150, 243),  // 蓝
        QColor(76, 175, 80),   // 绿
        QColor(255, 152, 0),   // 橙
        QColor(156, 39, 176),  // 紫
        QColor(0, 150, 136),   // 青
        QColor(121, 85, 72),   // 棕
        QColor(96, 125, 139),  // 蓝灰
    };
    return colors;
}

double SimpleChartWidget::currentTimeSec()
{
    if (!chartTimer().isValid()) chartTimer().start();
    const double tSec = chartTimer().elapsed() / 1000.0;

    // 初始化或防止时间倒退
    if (!m_timeInited) {
        m_timeInited = true;
        m_lastT = tSec;
    } else if (tSec < m_lastT) {
        m_lastT = tSec;
    } else {
        m_lastT = tSec;
    }
    return m_lastT;
}

void SimpleChartWidget::ensureSeries(const QString &seriesId, const QString &displayName)
{
    if (seriesId.isEmpty()) return;
    if (m_series.contains(seriesId)) {
        if (!displayName.isEmpty())
            m_series[seriesId].displayName = displayName;
        return;
    }

    SeriesData sd;
    sd.displayName = displayName.isEmpty() ? seriesId : displayName;
    const int idx = m_seriesOrder.size() % palette().size();
    sd.color = palette().at(idx);
    m_series.insert(seriesId, sd);
    m_seriesOrder.append(seriesId);
}

void SimpleChartWidget::trimToWindow(const QString &seriesId)
{
    auto it = m_series.find(seriesId);
    if (it == m_series.end()) return;
    const double tSec = m_lastT;
    const double minT = tSec - m_windowSeconds;
    auto &pts = it.value().points;
    while (!pts.isEmpty() && pts.first().x() < minT) {
        pts.removeFirst();
    }
}

void SimpleChartWidget::appendPoint(const QString &seriesId, double value)
{
    if (seriesId.isEmpty()) return;
    if (!m_series.contains(seriesId))
        ensureSeries(seriesId, seriesId);

    const double tSec = currentTimeSec();
    auto &pts = m_series[seriesId].points;
    pts.append(QPointF(tSec, value));
    trimToWindow(seriesId);
    update();
}

void SimpleChartWidget::appendPoint(double value)
{
    appendPoint(QStringLiteral("default"), value);
}

void SimpleChartWidget::clearSeries(const QString &seriesId)
{
    if (!m_series.contains(seriesId)) return;
    m_series[seriesId].points.clear();
    update();
}

void SimpleChartWidget::clear()
{
    for (auto it = m_series.begin(); it != m_series.end(); ++it) {
        it.value().points.clear();
    }
    m_timeInited = false;
    m_lastT = 0.0;
    update();
}

void SimpleChartWidget::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);
    QPainter p(this);
    p.setRenderHint(QPainter::Antialiasing);
    p.setRenderHint(QPainter::SmoothPixmapTransform);

    QRectF rect = this->rect().adjusted(60, 20, -30, -40);
    if (rect.width() <= 0 || rect.height() <= 0) return;

    p.fillRect(this->rect(), QColor(250, 250, 250));
    p.setPen(QColor(220, 220, 220));
    p.drawRect(rect);

    // 计算是否有可画的系列
    int drawableSeries = 0;
    for (const auto &sd : m_series) {
        if (sd.points.size() >= 2) drawableSeries++;
    }
    if (drawableSeries == 0) return;

    double yMin = 0, yMax = 100;
    bool yInited = false;
    double xMax = 0.0;
    bool xInited = false;

    for (const auto &sd : m_series) {
        if (sd.points.size() < 2) continue;
        for (const QPointF &pt : sd.points) {
            if (!yInited) {
                yMin = yMax = pt.y();
                yInited = true;
            } else {
                yMin = qMin(yMin, pt.y());
                yMax = qMax(yMax, pt.y());
            }
        }
        const double lastX = sd.points.last().x();
        if (!xInited) {
            xMax = lastX;
            xInited = true;
        } else {
            xMax = qMax(xMax, lastX);
        }
    }

    double range = yMax - yMin;
    if (range < 10) range = 10;
    yMin = yMin - range * 0.1;
    yMax = yMax + range * 0.1;
    range = yMax - yMin;

    const double xMin = qMax(xMax - m_windowSeconds, 0.0);
    double xRange = xMax - xMin;
    if (xRange < 1e-6) xRange = 1e-6;

    // 画坐标轴和刻度
    p.setPen(QPen(QColor(180, 180, 180), 1));

    // Y 轴
    p.drawLine(QPointF(rect.left(), rect.top()), QPointF(rect.left(), rect.bottom()));
    // X 轴
    p.drawLine(QPointF(rect.left(), rect.bottom()), QPointF(rect.right(), rect.bottom()));

    QFontMetrics fm(p.font());

    // Y 轴刻度（3 个：min/mid/max）
    for (int i = 0; i <= 2; ++i) {
        double t = i / 2.0;
        double yVal = yMax - t * (yMax - yMin);
        double y = rect.top() + t * rect.height();
        p.setPen(QColor(220, 220, 220));
        p.drawLine(QPointF(rect.left(), y), QPointF(rect.right(), y));
        p.setPen(QColor(130, 130, 130));
        QString label = QString::number(yVal, 'f', 1);
        int w = fm.horizontalAdvance(label);
        p.drawText(rect.left() - w - 6, y + fm.ascent() / 2, label);
    }

    // X 轴刻度（时间）：显示真实滚动窗口 [xMin, xMax]
    const double totalTime = xRange;
    const int xTicks = 4;
    for (int i = 0; i <= xTicks; ++i) {
        double t = i / static_cast<double>(xTicks);
        double x = rect.left() + t * rect.width();
        p.setPen(QColor(220, 220, 220));
        p.drawLine(QPointF(x, rect.top()), QPointF(x, rect.bottom()));
        p.setPen(QColor(130, 130, 130));
        double sec = xMin + totalTime * t;
        QString label = QString::number(sec, 'f', 1);
        int w = fm.horizontalAdvance(label);
        p.drawText(x - w / 2, rect.bottom() + fm.height() + 2, label);
    }

    // 坐标轴标题
    p.setPen(QColor(80, 80, 80));
    p.drawText(rect.center().x() - 20, this->rect().bottom() - 5, QString::fromUtf8("时间 (s)"));
    p.save();
    p.translate(this->rect().left() + 10, rect.center().y());
    p.rotate(-90);
    p.drawText(0, 0, QString::fromUtf8("滤波值"));
    p.restore();

    // 绘制多条曲线
    for (const QString &seriesId : m_seriesOrder) {
        if (!m_series.contains(seriesId)) continue;
        const auto &sd = m_series[seriesId];
        if (sd.points.size() < 2) continue;

        QPolygonF poly;
        poly.reserve(sd.points.size());
        for (const QPointF &pt : sd.points) {
            const double x = rect.left() + (pt.x() - xMin) / xRange * rect.width();
            const double y = rect.bottom() - (pt.y() - yMin) / range * rect.height();
            poly << QPointF(x, y);
        }

        p.setPen(QPen(sd.color, 2));
        p.setBrush(Qt::NoBrush);
        p.drawPolyline(poly);
    }

    // 图例（右上角）
    {
        const int legendX = static_cast<int>(rect.right()) - 170;
        int legendY = static_cast<int>(rect.top()) + 6;
        const int lineW = 18;
        const int lineH = 10;
        const int gap = 4;

        p.setPen(QPen(QColor(60, 60, 60), 1));
        for (const QString &seriesId : m_seriesOrder) {
            if (!m_series.contains(seriesId)) continue;
            const auto &sd = m_series[seriesId];
            if (sd.points.size() < 2) continue;
            p.setPen(QPen(sd.color, 2));
            p.drawLine(QPointF(legendX, legendY + lineH / 2), QPointF(legendX + lineW, legendY + lineH / 2));
            p.setPen(QPen(QColor(60, 60, 60), 1));
            p.drawText(legendX + lineW + 6, legendY + lineH, sd.displayName);
            legendY += (lineH + gap);
            if (legendY > rect.bottom() - 10) break; // 防止太长溢出
        }
    }
}
