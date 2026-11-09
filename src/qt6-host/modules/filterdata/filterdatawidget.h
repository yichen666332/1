#ifndef FILTERDATAWIDGET_H
#define FILTERDATAWIDGET_H

#include <QWidget>
#include <QLabel>

struct FilterDataPacket;

class FilterDataWidget : public QWidget
{
    Q_OBJECT

public:
    explicit FilterDataWidget(QWidget *parent = nullptr);
    ~FilterDataWidget();

public slots:
    void onDataUpdated(const FilterDataPacket &packet);

private:
    void setupUi();
    QWidget *createDataBlock(const QString &title, const QString &ultrasonicMeas, 
                             const QString &laserMeas, const QString &ultrasonicFilt, 
                             const QString &laserFilt);

    QLabel *m_labelRawUltrasonicMeas;
    QLabel *m_labelRawLaserMeas;
    QLabel *m_labelRawUltrasonicFilt;
    QLabel *m_labelRawLaserFilt;

    QLabel *m_labelKalmanUltrasonicMeas;
    QLabel *m_labelKalmanLaserMeas;
    QLabel *m_labelKalmanUltrasonicFilt;
    QLabel *m_labelKalmanLaserFilt;

    QLabel *m_labelAdaptiveUltrasonicMeas;
    QLabel *m_labelAdaptiveLaserMeas;
    QLabel *m_labelAdaptiveUltrasonicFilt;
    QLabel *m_labelAdaptiveLaserFilt;

    QLabel *m_labelHinfUltrasonicMeas;
    QLabel *m_labelHinfLaserMeas;
    QLabel *m_labelHinfUltrasonicFilt;
    QLabel *m_labelHinfLaserFilt;
};

#endif // FILTERDATAWIDGET_H
