#ifndef SERIALFRAMEPARSER_H
#define SERIALFRAMEPARSER_H

#include <QObject>
#include <QByteArray>

class SerialFrameParser : public QObject
{
    Q_OBJECT

public:
    explicit SerialFrameParser(int bufferSize = 1024, QObject *parent = nullptr);

    void append(const QByteArray &data);
    void reset();

signals:
    // raw, filtered, tofRaw, tofFiltered
    void frameDecoded(float raw, float filtered, float tofRaw, float tofFiltered);

private:
    void tryParseFrames();
    void consume(int n);

    static constexpr quint8 HEAD = 0xAA;
    static constexpr quint8 TAIL = 0x55;
    static constexpr int FRAME_SIZE = 19;   // AA + 16 data + XOR + 55

    QByteArray m_buf;
    int m_head = 0; // next write index
    int m_tail = 0; // next read index
    int m_len = 0;  // valid bytes count
};

#endif // SERIALFRAMEPARSER_H
