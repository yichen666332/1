#include "filterdatawidget.h"
#include "core/filterdatapacket.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QFrame>
#include <QGridLayout>
#include <QSizePolicy>

FilterDataWidget::FilterDataWidget(QWidget *parent)
    : QWidget(parent)
    , m_labelRawUltrasonicMeas(nullptr)
    , m_labelRawLaserMeas(nullptr)
    , m_labelRawUltrasonicFilt(nullptr)
    , m_labelRawLaserFilt(nullptr)
    , m_labelKalmanUltrasonicMeas(nullptr)
    , m_labelKalmanLaserMeas(nullptr)
    , m_labelKalmanUltrasonicFilt(nullptr)
    , m_labelKalmanLaserFilt(nullptr)
    , m_labelAdaptiveUltrasonicMeas(nullptr)
    , m_labelAdaptiveLaserMeas(nullptr)
    , m_labelAdaptiveUltrasonicFilt(nullptr)
    , m_labelAdaptiveLaserFilt(nullptr)
    , m_labelHinfUltrasonicMeas(nullptr)
    , m_labelHinfLaserMeas(nullptr)
    , m_labelHinfUltrasonicFilt(nullptr)
    , m_labelHinfLaserFilt(nullptr)
{
    setupUi();
}

FilterDataWidget::~FilterDataWidget()
{
}

void FilterDataWidget::setupUi()
{
    QVBoxLayout *mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(20, 20, 20, 20);
    mainLayout->setSpacing(16);

    QGroupBox *rawGroup = new QGroupBox(QString::fromUtf8("原始数据"), this);
    rawGroup->setStyleSheet(
        "QGroupBox {"
        "  font-size: 15px;"
        "  font-weight: bold;"
        "  border: 2px solid #CCCCCC;"
        "  border-radius: 8px;"
        "  margin-top: 12px;"
        "  padding-top: 12px;"
        "}"
        "QGroupBox::title {"
        "  subcontrol-origin: margin;"
        "  left: 12px;"
        "  padding: 0 4px;"
        "}"
        );
    // 修改为网格布局
    QGridLayout *rawLayout = new QGridLayout(rawGroup);
    rawLayout->setSpacing(12); // 增加间距
    rawLayout->setContentsMargins(16, 24, 16, 16);

    m_labelRawUltrasonicMeas = new QLabel(QString::fromUtf8("超声波测量数据: 0"), rawGroup);
    m_labelRawLaserMeas = new QLabel(QString::fromUtf8("激光测量数据: 0"), rawGroup);
    m_labelRawUltrasonicFilt = new QLabel(QString::fromUtf8("超声波滤波数据: 0"), rawGroup);
    m_labelRawLaserFilt = new QLabel(QString::fromUtf8("激光滤波数据: 0"), rawGroup);

    // 设置字体大小
    QFont font = m_labelRawUltrasonicMeas->font();
    font.setPointSize(13);
    m_labelRawUltrasonicMeas->setFont(font);
    m_labelRawLaserMeas->setFont(font);
    m_labelRawUltrasonicFilt->setFont(font);
    m_labelRawLaserFilt->setFont(font);

    // 2×2网格布局
    rawLayout->addWidget(m_labelRawUltrasonicMeas, 0, 0);
    rawLayout->addWidget(m_labelRawLaserMeas, 0, 1);
    rawLayout->addWidget(m_labelRawUltrasonicFilt, 1, 0);
    rawLayout->addWidget(m_labelRawLaserFilt, 1, 1);

    // 移除拉伸，让内容占满空间
    // rawLayout->setRowStretch(2, 1);
    // rawLayout->setColumnStretch(2, 1);

    rawGroup->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);

    QGroupBox *kalmanGroup = new QGroupBox(QString::fromUtf8("卡尔曼滤波数据"), this);
    kalmanGroup->setStyleSheet(rawGroup->styleSheet());
    // 修改为网格布局
    QGridLayout *kalmanLayout = new QGridLayout(kalmanGroup);
    kalmanLayout->setSpacing(12); // 增加间距
    kalmanLayout->setContentsMargins(16, 24, 16, 16);

    m_labelKalmanUltrasonicMeas = new QLabel(QString::fromUtf8("超声波测量数据: 0"), kalmanGroup);
    m_labelKalmanLaserMeas = new QLabel(QString::fromUtf8("激光测量数据: 0"), kalmanGroup);
    m_labelKalmanUltrasonicFilt = new QLabel(QString::fromUtf8("超声波滤波数据: 0"), kalmanGroup);
    m_labelKalmanLaserFilt = new QLabel(QString::fromUtf8("激光滤波数据: 0"), kalmanGroup);

    // 设置字体大小
    m_labelKalmanUltrasonicMeas->setFont(font);
    m_labelKalmanLaserMeas->setFont(font);
    m_labelKalmanUltrasonicFilt->setFont(font);
    m_labelKalmanLaserFilt->setFont(font);

    // 2×2网格布局
    kalmanLayout->addWidget(m_labelKalmanUltrasonicMeas, 0, 0);
    kalmanLayout->addWidget(m_labelKalmanLaserMeas, 0, 1);
    kalmanLayout->addWidget(m_labelKalmanUltrasonicFilt, 1, 0);
    kalmanLayout->addWidget(m_labelKalmanLaserFilt, 1, 1);

    // 移除拉伸
    // kalmanLayout->setRowStretch(2, 1);
    // kalmanLayout->setColumnStretch(2, 1);

    kalmanGroup->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);

    QGroupBox *adaptiveGroup = new QGroupBox(QString::fromUtf8("自适应低通数据"), this);
    adaptiveGroup->setStyleSheet(rawGroup->styleSheet());
    // 修改为网格布局
    QGridLayout *adaptiveLayout = new QGridLayout(adaptiveGroup);
    adaptiveLayout->setSpacing(12); // 增加间距
    adaptiveLayout->setContentsMargins(16, 24, 16, 16);

    m_labelAdaptiveUltrasonicMeas = new QLabel(QString::fromUtf8("超声波测量数据: 0"), adaptiveGroup);
    m_labelAdaptiveLaserMeas = new QLabel(QString::fromUtf8("激光测量数据: 0"), adaptiveGroup);
    m_labelAdaptiveUltrasonicFilt = new QLabel(QString::fromUtf8("超声波滤波数据: 0"), adaptiveGroup);
    m_labelAdaptiveLaserFilt = new QLabel(QString::fromUtf8("激光滤波数据: 0"), adaptiveGroup);

    // 设置字体大小
    m_labelAdaptiveUltrasonicMeas->setFont(font);
    m_labelAdaptiveLaserMeas->setFont(font);
    m_labelAdaptiveUltrasonicFilt->setFont(font);
    m_labelAdaptiveLaserFilt->setFont(font);

    // 2×2网格布局
    adaptiveLayout->addWidget(m_labelAdaptiveUltrasonicMeas, 0, 0);
    adaptiveLayout->addWidget(m_labelAdaptiveLaserMeas, 0, 1);
    adaptiveLayout->addWidget(m_labelAdaptiveUltrasonicFilt, 1, 0);
    adaptiveLayout->addWidget(m_labelAdaptiveLaserFilt, 1, 1);

    // 移除拉伸
    // adaptiveLayout->setRowStretch(2, 1);
    // adaptiveLayout->setColumnStretch(2, 1);

    adaptiveGroup->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);

    QGroupBox *hinfGroup = new QGroupBox(QString::fromUtf8("H∞滤波数据"), this);
    hinfGroup->setStyleSheet(rawGroup->styleSheet());
    // 修改为网格布局
    QGridLayout *hinfLayout = new QGridLayout(hinfGroup);
    hinfLayout->setSpacing(12); // 增加间距
    hinfLayout->setContentsMargins(16, 24, 16, 16);

    m_labelHinfUltrasonicMeas = new QLabel(QString::fromUtf8("超声波测量数据: 0"), hinfGroup);
    m_labelHinfLaserMeas = new QLabel(QString::fromUtf8("激光测量数据: 0"), hinfGroup);
    m_labelHinfUltrasonicFilt = new QLabel(QString::fromUtf8("超声波滤波数据: 0"), hinfGroup);
    m_labelHinfLaserFilt = new QLabel(QString::fromUtf8("激光滤波数据: 0"), hinfGroup);

    // 设置字体大小
    m_labelHinfUltrasonicMeas->setFont(font);
    m_labelHinfLaserMeas->setFont(font);
    m_labelHinfUltrasonicFilt->setFont(font);
    m_labelHinfLaserFilt->setFont(font);

    // 2×2网格布局
    hinfLayout->addWidget(m_labelHinfUltrasonicMeas, 0, 0);
    hinfLayout->addWidget(m_labelHinfLaserMeas, 0, 1);
    hinfLayout->addWidget(m_labelHinfUltrasonicFilt, 1, 0);
    hinfLayout->addWidget(m_labelHinfLaserFilt, 1, 1);

    // 移除拉伸
    // hinfLayout->setRowStretch(2, 1);
    // hinfLayout->setColumnStretch(2, 1);

    hinfGroup->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);

    // 垂直排列四个分组
    mainLayout->addWidget(rawGroup);
    mainLayout->addWidget(kalmanGroup);
    mainLayout->addWidget(adaptiveGroup);
    mainLayout->addWidget(hinfGroup);
}

QWidget *FilterDataWidget::createDataBlock(const QString &title, const QString &ultrasonicMeas, 
                                           const QString &laserMeas, const QString &ultrasonicFilt, 
                                           const QString &laserFilt)
{
    Q_UNUSED(title);
    Q_UNUSED(ultrasonicMeas);
    Q_UNUSED(laserMeas);
    Q_UNUSED(ultrasonicFilt);
    Q_UNUSED(laserFilt);
    return nullptr;
}

void FilterDataWidget::onDataUpdated(const FilterDataPacket &packet)
{
    m_labelRawUltrasonicMeas->setText(QString::fromUtf8("超声波测量数据: %1").arg(packet.rawUltrasonicMeas, 0, 'f', 2));
    m_labelRawLaserMeas->setText(QString::fromUtf8("激光测量数据: %1").arg(packet.rawLaserMeas, 0, 'f', 2));
    m_labelRawUltrasonicFilt->setText(QString::fromUtf8("超声波滤波数据: %1").arg(packet.rawUltrasonicFilt, 0, 'f', 2));
    m_labelRawLaserFilt->setText(QString::fromUtf8("激光滤波数据: %1").arg(packet.rawLaserFilt, 0, 'f', 2));

    m_labelKalmanUltrasonicMeas->setText(QString::fromUtf8("超声波测量数据: %1").arg(packet.kalmanUltrasonicMeas, 0, 'f', 2));
    m_labelKalmanLaserMeas->setText(QString::fromUtf8("激光测量数据: %1").arg(packet.kalmanLaserMeas, 0, 'f', 2));
    m_labelKalmanUltrasonicFilt->setText(QString::fromUtf8("超声波滤波数据: %1").arg(packet.kalmanUltrasonicFilt, 0, 'f', 2));
    m_labelKalmanLaserFilt->setText(QString::fromUtf8("激光滤波数据: %1").arg(packet.kalmanLaserFilt, 0, 'f', 2));

    m_labelAdaptiveUltrasonicMeas->setText(QString::fromUtf8("超声波测量数据: %1").arg(packet.adaptiveUltrasonicMeas, 0, 'f', 2));
    m_labelAdaptiveLaserMeas->setText(QString::fromUtf8("激光测量数据: %1").arg(packet.adaptiveLaserMeas, 0, 'f', 2));
    m_labelAdaptiveUltrasonicFilt->setText(QString::fromUtf8("超声波滤波数据: %1").arg(packet.adaptiveUltrasonicFilt, 0, 'f', 2));
    m_labelAdaptiveLaserFilt->setText(QString::fromUtf8("激光滤波数据: %1").arg(packet.adaptiveLaserFilt, 0, 'f', 2));

    m_labelHinfUltrasonicMeas->setText(QString::fromUtf8("超声波测量数据: %1").arg(packet.hinfUltrasonicMeas, 0, 'f', 2));
    m_labelHinfLaserMeas->setText(QString::fromUtf8("激光测量数据: %1").arg(packet.hinfLaserMeas, 0, 'f', 2));
    m_labelHinfUltrasonicFilt->setText(QString::fromUtf8("超声波滤波数据: %1").arg(packet.hinfUltrasonicFilt, 0, 'f', 2));
    m_labelHinfLaserFilt->setText(QString::fromUtf8("激光滤波数据: %1").arg(packet.hinfLaserFilt, 0, 'f', 2));
}
