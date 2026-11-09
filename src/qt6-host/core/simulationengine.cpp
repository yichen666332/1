#include "simulationengine.h"
#include <QRandomGenerator>
#include <QtMath>

SimulationEngine::SimulationEngine(QObject *parent)
    : QObject(parent)
    , m_running(false)
    , m_rng(QRandomGenerator::global()->generate())
    , m_baseValue(98.0)
{
    m_timer = new QTimer(this);
    m_timer->setInterval(100);
    connect(m_timer, &QTimer::timeout, this, &SimulationEngine::onTimerTick);
}

SimulationEngine::~SimulationEngine()
{
    stop();
}

void SimulationEngine::start()
{
    if (m_running) return;
    m_running = true;
    m_baseValue = 95.0 + (QRandomGenerator::global()->bounded(100) / 10.0);
    m_timer->start();
}

void SimulationEngine::stop()
{
    if (!m_running) return;
    m_running = false;
    m_timer->stop();
}

double SimulationEngine::generateValue(double base, double noise)
{
    std::uniform_real_distribution<double> dist(-noise, noise);
    double val = base + dist(m_rng);
    return qBound(0.0, val, 150.0);
}

void SimulationEngine::generatePacket(FilterDataPacket &packet)
{
    m_baseValue += (QRandomGenerator::global()->bounded(20) - 10) / 100.0;
    m_baseValue = qBound(90.0, m_baseValue, 105.0);

    double rawU = generateValue(m_baseValue, 1.5);
    double rawL = generateValue(m_baseValue, 1.2);
    packet.rawUltrasonicMeas = rawU;
    packet.rawLaserMeas = rawL;
    packet.rawUltrasonicFilt = generateValue(rawU, 0.3);
    packet.rawLaserFilt = generateValue(rawL, 0.3);

    packet.kalmanUltrasonicMeas = generateValue(m_baseValue, 1.0);
    packet.kalmanLaserMeas = generateValue(m_baseValue, 1.0);
    packet.kalmanUltrasonicFilt = generateValue(packet.rawUltrasonicFilt, 0.2);
    packet.kalmanLaserFilt = generateValue(packet.rawLaserFilt, 0.2);

    packet.adaptiveUltrasonicMeas = generateValue(m_baseValue, 1.2);
    packet.adaptiveLaserMeas = generateValue(m_baseValue, 1.1);
    packet.adaptiveUltrasonicFilt = generateValue(packet.rawUltrasonicFilt, 0.25);
    packet.adaptiveLaserFilt = generateValue(packet.rawLaserFilt, 0.25);

    packet.hinfUltrasonicMeas = generateValue(m_baseValue, 1.3);
    packet.hinfLaserMeas = generateValue(m_baseValue, 1.2);
    packet.hinfUltrasonicFilt = generateValue(packet.rawUltrasonicFilt, 0.2);
    packet.hinfLaserFilt = generateValue(packet.rawLaserFilt, 0.2);
}

void SimulationEngine::onTimerTick()
{
    FilterDataPacket packet;
    generatePacket(packet);
    emit dataUpdated(packet);
}
