#ifndef SIMULATIONENGINE_H
#define SIMULATIONENGINE_H

#include "filterdatapacket.h"
#include <QObject>
#include <QTimer>
#include <random>

class SimulationEngine : public QObject
{
    Q_OBJECT

public:
    explicit SimulationEngine(QObject *parent = nullptr);
    ~SimulationEngine();

    void start();
    void stop();
    bool isRunning() const { return m_running; }

signals:
    void dataUpdated(const FilterDataPacket &packet);

private slots:
    void onTimerTick();

private:
    double generateValue(double base, double noise);
    void generatePacket(FilterDataPacket &packet);

    QTimer *m_timer;
    bool m_running;
    std::mt19937 m_rng;
    double m_baseValue;
};

#endif // SIMULATIONENGINE_H
