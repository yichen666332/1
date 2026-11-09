#include "curveanalysiswidget.h"
#include "simplechartwidget.h"
#include "combined/combinedfilterwidget.h"
#include "core/filterdatapacket.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QTreeWidgetItem>
#include <QHeaderView>
#include <QFrame>
#include <QLabel>
#include <QSignalBlocker>
#include <QStringList>
#include <cmath>

CurveAnalysisWidget::CurveAnalysisWidget(QWidget *parent)
    : QWidget(parent)
    , m_dataTree(nullptr)
    , m_chartWidget(nullptr)
    , m_chart(nullptr)
    , m_scoreLabel(nullptr)
{
    setupUi();
}

CurveAnalysisWidget::~CurveAnalysisWidget()
{
}

void CurveAnalysisWidget::setupUi()
{
    QVBoxLayout *mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(16, 16, 16, 16);
    mainLayout->setSpacing(12);

    QHBoxLayout *contentLayout = new QHBoxLayout();
    contentLayout->setSpacing(16);

    setupDataTree();
    setupChartArea();

    contentLayout->addWidget(m_dataTree, 0);
    // 右侧增加一个“组合滤波”页签，不影响原有页
    m_tabs = new QTabWidget(this);
    m_tabs->setStyleSheet(
        "QTabWidget::pane { border: none; }"
        "QTabBar::tab { padding: 8px 14px; font-size: 13px; }"
    );
    m_tabs->addTab(m_chartWidget, QString::fromUtf8("基础曲线"));

    m_combined = new CombinedFilterWidget(this);
    m_tabs->addTab(m_combined, QString::fromUtf8("组合滤波"));

    contentLayout->addWidget(m_tabs, 1);

    mainLayout->addLayout(contentLayout);
}

void CurveAnalysisWidget::setupDataTree()
{
    m_dataTree = new QTreeWidget(this);
    m_dataTree->setHeaderLabel(QString::fromUtf8("曲线选择（可多选）"));
    m_dataTree->setFixedWidth(220);
    m_dataTree->setStyleSheet(
        "QTreeWidget {"
        "  border: 1px solid #E0E0E0;"
        "  border-radius: 4px;"
        "  background-color: #FAFAFA;"
        "}"
    );

    m_dataTree->setRootIsDecorated(true);
    m_dataTree->setItemsExpandable(true);
    m_dataTree->setExpandsOnDoubleClick(true);

    auto addChild = [&](QTreeWidgetItem *parent, const QString &text, const QString &seriesId, const QString &displayName) {
        QTreeWidgetItem *ch = new QTreeWidgetItem(parent);
        ch->setText(0, text);
        ch->setFlags(ch->flags() | Qt::ItemIsUserCheckable);
        ch->setCheckState(0, Qt::Unchecked);
        ch->setData(0, Qt::UserRole, seriesId);
        ch->setData(0, Qt::UserRole + 1, displayName);
        return ch;
    };

    auto addGroup = [&](const QString &groupText, const QString &prefix) {
        QTreeWidgetItem *g = new QTreeWidgetItem(m_dataTree);
        g->setText(0, groupText);
        g->setFlags(g->flags() & ~Qt::ItemIsUserCheckable);

        addChild(g, QString::fromUtf8("Raw"),     prefix + QStringLiteral(".Raw"),     groupText + QString::fromUtf8(" / Raw"));
        addChild(g, QString::fromUtf8("Filt"),    prefix + QStringLiteral(".Filt"),    groupText + QString::fromUtf8(" / Filt"));
        addChild(g, QString::fromUtf8("TOFraw"),  prefix + QStringLiteral(".TOFraw"),  groupText + QString::fromUtf8(" / TOFraw"));
        addChild(g, QString::fromUtf8("TOFFilt"), prefix + QStringLiteral(".TOFFilt"), groupText + QString::fromUtf8(" / TOFFilt"));
        g->setExpanded(true);
    };

    // 4 类 × 4 选项
    addGroup(QString::fromUtf8("原始数据"), QStringLiteral("raw"));
    addGroup(QString::fromUtf8("卡尔曼滤波"), QStringLiteral("kalman"));
    addGroup(QString::fromUtf8("自适应低通滤波"), QStringLiteral("adaptive"));
    addGroup(QString::fromUtf8("H∞滤波"), QStringLiteral("hinf"));

    connect(m_dataTree, &QTreeWidget::itemChanged, this, &CurveAnalysisWidget::onTreeItemChanged);
}

void CurveAnalysisWidget::setupChartArea()
{
    QFrame *chartFrame = new QFrame(this);
    chartFrame->setFrameShape(QFrame::Box);
    chartFrame->setStyleSheet(
        "QFrame {"
        "  background-color: white;"
        "  border: 1px solid #CCCCCC;"
        "  border-radius: 4px;"
        "}"
    );

    QVBoxLayout *chartLayout = new QVBoxLayout(chartFrame);
    chartLayout->setContentsMargins(16, 16, 16, 16);

    QLabel *chartTitle = new QLabel(QString::fromUtf8("滤波实时数据"), chartFrame);
    chartTitle->setStyleSheet("font-size: 15px; font-weight: bold;");
    chartLayout->addWidget(chartTitle);

    m_scoreLabel = new QLabel(chartFrame);
    m_scoreLabel->setStyleSheet("color: #555555; font-size: 13px;");
    m_scoreLabel->setText(QString::fromUtf8("评分统计：样本不足，正在积累数据..."));
    chartLayout->addWidget(m_scoreLabel);

    m_chart = new SimpleChartWidget(chartFrame);
    m_chart->setMinimumHeight(350);
    m_chart->setWindowSeconds(20.0);
    chartLayout->addWidget(m_chart);

    m_chartWidget = chartFrame;
}

void CurveAnalysisWidget::clearChart()
{
    if (m_chart)
        m_chart->clear();
    if (m_combined)
        m_combined->clearChart();

    m_oefScorers.clear();
    m_rhfScorers.clear();
    for (const QString &sid : m_enabledSeries) {
        m_oefScorers.insert(sid, OefScorerQt(256));
        m_rhfScorers.insert(sid, RhfScorerQt(256));
    }
    updateScoreLabel();
}

void CurveAnalysisWidget::onTreeItemChanged(QTreeWidgetItem *item, int column)
{
    Q_UNUSED(column);
    if (!item) return;
    const QString seriesId = item->data(0, Qt::UserRole).toString();
    const QString displayName = item->data(0, Qt::UserRole + 1).toString();
    if (seriesId.isEmpty()) return; // group item

    const bool checked = (item->checkState(0) == Qt::Checked);
    if (checked) {
        m_enabledSeries.insert(seriesId);
        m_seriesDisplayNames.insert(seriesId, displayName);
        m_oefScorers.insert(seriesId, OefScorerQt(256));
        m_rhfScorers.insert(seriesId, RhfScorerQt(256));
        if (m_chart) m_chart->ensureSeries(seriesId, displayName);
    } else {
        m_enabledSeries.remove(seriesId);
        m_seriesDisplayNames.remove(seriesId);
        m_oefScorers.remove(seriesId);
        m_rhfScorers.remove(seriesId);
        if (m_chart) m_chart->clearSeries(seriesId);
    }
    updateScoreLabel();
}

double CurveAnalysisWidget::valueForSeries(const QString &seriesId, const FilterDataPacket &packet) const
{
    // seriesId: raw/kalman/adaptive/hinf + .Raw/.Filt/.TOFraw/.TOFFilt
    if (seriesId == QLatin1String("raw.Raw")) return packet.rawUltrasonicMeas;
    if (seriesId == QLatin1String("raw.Filt")) return packet.rawUltrasonicFilt;
    if (seriesId == QLatin1String("raw.TOFraw")) return packet.rawLaserMeas;
    if (seriesId == QLatin1String("raw.TOFFilt")) return packet.rawLaserFilt;

    if (seriesId == QLatin1String("kalman.Raw")) return packet.kalmanUltrasonicMeas;
    if (seriesId == QLatin1String("kalman.Filt")) return packet.kalmanUltrasonicFilt;
    if (seriesId == QLatin1String("kalman.TOFraw")) return packet.kalmanLaserMeas;
    if (seriesId == QLatin1String("kalman.TOFFilt")) return packet.kalmanLaserFilt;

    if (seriesId == QLatin1String("adaptive.Raw")) return packet.adaptiveUltrasonicMeas;
    if (seriesId == QLatin1String("adaptive.Filt")) return packet.adaptiveUltrasonicFilt;
    if (seriesId == QLatin1String("adaptive.TOFraw")) return packet.adaptiveLaserMeas;
    if (seriesId == QLatin1String("adaptive.TOFFilt")) return packet.adaptiveLaserFilt;

    if (seriesId == QLatin1String("hinf.Raw")) return packet.hinfUltrasonicMeas;
    if (seriesId == QLatin1String("hinf.Filt")) return packet.hinfUltrasonicFilt;
    if (seriesId == QLatin1String("hinf.TOFraw")) return packet.hinfLaserMeas;
    if (seriesId == QLatin1String("hinf.TOFFilt")) return packet.hinfLaserFilt;

    return 0.0;
}

void CurveAnalysisWidget::updateScoreLabel()
{
    if (!m_scoreLabel) return;
    if (m_enabledSeries.isEmpty()) {
        m_scoreLabel->setText(QString::fromUtf8("评分统计：请先勾选需要评估的曲线。"));
        return;
    }

    QStringList lines;
    QStringList ids = m_enabledSeries.values();
    ids.sort();
    for (const QString &sid : ids) {
        const auto oefIt = m_oefScorers.constFind(sid);
        const auto rhfIt = m_rhfScorers.constFind(sid);
        if (oefIt == m_oefScorers.constEnd() || rhfIt == m_rhfScorers.constEnd()) {
            continue;
        }
        const OefScoreResultQt oefRes = oefIt.value().evaluate();
        const RhfScoreResultQt rhfRes = rhfIt.value().evaluate();
        const QString name = m_seriesDisplayNames.value(sid, sid);

        if (std::isnan(oefRes.score) || std::isnan(rhfRes.score)) {
            lines << QString::fromUtf8("OEF: 样本不足 | RHF: 样本不足（%1）").arg(name);
        } else {
            lines << QString::fromUtf8("OEF: %1 | RHF: %2（%3）")
                        .arg(oefRes.score, 0, 'f', 1)
                        .arg(rhfRes.score, 0, 'f', 1)
                        .arg(name);
        }
    }
    m_scoreLabel->setText(lines.join(QString::fromUtf8("\n")));
}

void CurveAnalysisWidget::onDataUpdated(const FilterDataPacket &packet)
{
    if (m_combined)
        m_combined->onDataUpdated(packet);

    // 多选曲线：勾选几个就画几条
    if (m_chart) {
        for (const QString &sid : m_enabledSeries) {
            const double v = valueForSeries(sid, packet);
            m_chart->appendPoint(sid, v);

            // 每个勾选项单独评分，但评分口径保持原逻辑不变：
            // OEF = raw vs adaptive，RHF = raw vs hinf（按超声/ToF通道区分）
            const bool isTof = sid.contains(QStringLiteral(".TOF"));
            const double rawRef = isTof ? packet.rawLaserMeas : packet.rawUltrasonicMeas;
            const double oefOut = isTof ? packet.adaptiveLaserFilt : packet.adaptiveUltrasonicFilt;
            const double rhfOut = isTof ? packet.hinfLaserFilt : packet.hinfUltrasonicFilt;
            auto oefIt = m_oefScorers.find(sid);
            auto rhfIt = m_rhfScorers.find(sid);
            if (oefIt != m_oefScorers.end()) {
                oefIt.value().feed(rawRef, oefOut);
            }
            if (rhfIt != m_rhfScorers.end()) {
                rhfIt.value().feed(rawRef, rhfOut);
            }
        }
    }
    updateScoreLabel();
}
