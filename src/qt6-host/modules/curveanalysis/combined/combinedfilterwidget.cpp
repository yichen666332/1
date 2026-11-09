#include "combinedfilterwidget.h"
#include "../simplechartwidget.h"
#include "../../../core/filterdatapacket.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFrame>

static QString seriesName(const QString &group, const QString &channel)
{
    return group + QString::fromUtf8(" / ") + channel;
}

CombinedFilterWidget::CombinedFilterWidget(QWidget *parent)
    : QWidget(parent)
    , m_kh_kal_u(0.01f, 1.0f, 0.95f)
    , m_kh_kal_t(0.01f, 1.0f, 0.95f)
    , m_kh_hinf_u(0.01f, 1.0f, 0.10f, 2.5f)
    , m_kh_hinf_t(0.01f, 1.0f, 0.10f, 2.5f)
    , m_mh_med_u(5)
    , m_mh_med_t(5)
    , m_mh_hinf_u(0.01f, 1.0f, 0.10f, 2.5f)
    , m_mh_hinf_t(0.01f, 1.0f, 0.10f, 2.5f)
    , m_mk_med_u(5)
    , m_mk_med_t(5)
    , m_mk_kal_u(0.01f, 1.0f, 0.95f)
    , m_mk_kal_t(0.01f, 1.0f, 0.95f)
{
    setupUi();
}

void CombinedFilterWidget::setupUi()
{
    QHBoxLayout *root = new QHBoxLayout(this);
    root->setContentsMargins(0, 0, 0, 0);
    root->setSpacing(16);

    setupTree();
    setupChart();

    root->addWidget(m_tree, 0);

    QFrame *right = new QFrame(this);
    right->setFrameShape(QFrame::Box);
    right->setStyleSheet(
        "QFrame {"
        "  background-color: white;"
        "  border: 1px solid #CCCCCC;"
        "  border-radius: 4px;"
        "}"
    );
    QVBoxLayout *vl = new QVBoxLayout(right);
    vl->setContentsMargins(16, 16, 16, 16);
    vl->setSpacing(10);

    QLabel *title = new QLabel(QString::fromUtf8("组合滤波曲线"), right);
    title->setStyleSheet("font-size: 15px; font-weight: bold;");
    vl->addWidget(title);

    m_hint = new QLabel(QString::fromUtf8("勾选左侧组合项后，将在下方图表叠加显示。"), right);
    m_hint->setStyleSheet("color:#666; font-size:13px;");
    vl->addWidget(m_hint);

    vl->addWidget(m_chart, 1);
    root->addWidget(right, 1);
}

void CombinedFilterWidget::setupTree()
{
    m_tree = new QTreeWidget(this);
    m_tree->setHeaderLabel(QString::fromUtf8("组合选择（可多选）"));
    m_tree->setFixedWidth(260);
    m_tree->setStyleSheet(
        "QTreeWidget {"
        "  border: 1px solid #E0E0E0;"
        "  border-radius: 4px;"
        "  background-color: #FAFAFA;"
        "}"
    );
    m_tree->setRootIsDecorated(true);
    m_tree->setItemsExpandable(true);
    m_tree->setExpandsOnDoubleClick(true);

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
        QTreeWidgetItem *g = new QTreeWidgetItem(m_tree);
        g->setText(0, groupText);
        g->setFlags(g->flags() & ~Qt::ItemIsUserCheckable);
        addChild(g, QString::fromUtf8("Ultrasonic"), prefix + QStringLiteral(".U"), seriesName(groupText, QString::fromUtf8("Ultrasonic")));
        addChild(g, QString::fromUtf8("ToF"),        prefix + QStringLiteral(".T"), seriesName(groupText, QString::fromUtf8("ToF")));
        g->setExpanded(true);
    };

    addGroup(QString::fromUtf8("卡尔曼 + H∞（切换）"), QStringLiteral("KH"));
    addGroup(QString::fromUtf8("H∞ + 中值（串行）"), QStringLiteral("MH"));
    addGroup(QString::fromUtf8("卡尔曼 + 中值（串行）"), QStringLiteral("MK"));

    connect(m_tree, &QTreeWidget::itemChanged, this, &CombinedFilterWidget::onTreeItemChanged);
}

void CombinedFilterWidget::setupChart()
{
    m_chart = new SimpleChartWidget(this);
    m_chart->setMinimumHeight(350);
    m_chart->setWindowSeconds(20.0);
}

void CombinedFilterWidget::onTreeItemChanged(QTreeWidgetItem *item, int column)
{
    Q_UNUSED(column);
    if (!item) return;
    const QString seriesId = item->data(0, Qt::UserRole).toString();
    const QString displayName = item->data(0, Qt::UserRole + 1).toString();
    if (seriesId.isEmpty()) return;

    const bool checked = item->checkState(0) == Qt::Checked;
    if (checked) {
        m_enabled.insert(seriesId);
        if (m_chart) m_chart->ensureSeries(seriesId, displayName);
    } else {
        m_enabled.remove(seriesId);
        if (m_chart) m_chart->clearSeries(seriesId);
    }
}

void CombinedFilterWidget::clearChart()
{
    if (m_chart) m_chart->clear();

    // 重置组合滤波器内部状态
    m_kh_kal_u.reset(false);
    m_kh_kal_t.reset(false);
    m_kh_hinf_u.reset();
    m_kh_hinf_t.reset();

    m_mh_med_u.reset();
    m_mh_med_t.reset();
    m_mh_hinf_u.reset();
    m_mh_hinf_t.reset();

    m_mk_med_u.reset();
    m_mk_med_t.reset();
    m_mk_kal_u.reset(false);
    m_mk_kal_t.reset(false);
}

double CombinedFilterWidget::valueForSeries(const QString &seriesId, double uMeas, double tMeas)
{
    // KH: Kalman+H∞ switching
    if (seriesId == QLatin1String("KH.U")) {
        const float u = static_cast<float>(uMeas);
        const float yKal = m_kh_kal_u.update(u);
        (void)m_kh_hinf_u.update(u);
        const float z = m_kh_kal_u.lastInnovationZ();
        return (std::fabs(z) > m_kh_switchZ) ? static_cast<double>(m_kh_hinf_u.lastOutput()) : static_cast<double>(yKal);
    }
    if (seriesId == QLatin1String("KH.T")) {
        const float t = static_cast<float>(tMeas);
        const float yKal = m_kh_kal_t.update(t);
        (void)m_kh_hinf_t.update(t);
        const float z = m_kh_kal_t.lastInnovationZ();
        return (std::fabs(z) > m_kh_switchZ) ? static_cast<double>(m_kh_hinf_t.lastOutput()) : static_cast<double>(yKal);
    }

    // MH: Median -> H∞
    if (seriesId == QLatin1String("MH.U")) {
        const double med = m_mh_med_u.update(uMeas);
        return static_cast<double>(m_mh_hinf_u.update(static_cast<float>(med)));
    }
    if (seriesId == QLatin1String("MH.T")) {
        const double med = m_mh_med_t.update(tMeas);
        return static_cast<double>(m_mh_hinf_t.update(static_cast<float>(med)));
    }

    // MK: Median -> Kalman
    if (seriesId == QLatin1String("MK.U")) {
        const double med = m_mk_med_u.update(uMeas);
        return static_cast<double>(m_mk_kal_u.update(static_cast<float>(med)));
    }
    if (seriesId == QLatin1String("MK.T")) {
        const double med = m_mk_med_t.update(tMeas);
        return static_cast<double>(m_mk_kal_t.update(static_cast<float>(med)));
    }

    return 0.0;
}

void CombinedFilterWidget::onDataUpdated(const FilterDataPacket &packet)
{
    // 组合滤波统一以“测量值”作为输入（不影响原有滤波链）
    const double uMeas = packet.rawUltrasonicMeas;
    const double tMeas = packet.rawLaserMeas;

    if (!m_chart) return;
    for (const QString &sid : m_enabled) {
        const double y = valueForSeries(sid, uMeas, tMeas);
        m_chart->appendPoint(sid, y);
    }
}

