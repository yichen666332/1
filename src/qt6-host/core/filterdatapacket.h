#ifndef FILTERDATAPACKET_H
#define FILTERDATAPACKET_H

struct FilterDataPacket
{
    double rawUltrasonicMeas = 0;
    double rawLaserMeas = 0;
    double rawUltrasonicFilt = 0;
    double rawLaserFilt = 0;

    double kalmanUltrasonicMeas = 0;
    double kalmanLaserMeas = 0;
    double kalmanUltrasonicFilt = 0;
    double kalmanLaserFilt = 0;

    double adaptiveUltrasonicMeas = 0;
    double adaptiveLaserMeas = 0;
    double adaptiveUltrasonicFilt = 0;
    double adaptiveLaserFilt = 0;

    double hinfUltrasonicMeas = 0;
    double hinfLaserMeas = 0;
    double hinfUltrasonicFilt = 0;
    double hinfLaserFilt = 0;
};

#endif // FILTERDATAPACKET_H
