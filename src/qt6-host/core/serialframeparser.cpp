#include "serialframeparser.h"

#include <QtEndian>
#include <algorithm>
#include <cstring>

static float readFloatLE(const quint8 *p)
{
    quint32 u = qFromLittleEndian<quint32>(p);
    float f = 0.0f;
    static_assert(sizeof(float) == sizeof(quint32), "float must be 32-bit");
    std::memcpy(&f, &u, sizeof(float));
    return f;
}

SerialFrameParser::SerialFrameParser(int bufferSize, QObject *parent)
    : QObject(parent)
{
    if (bufferSize < 4 * FRAME_SIZE) bufferSize = 4 * FRAME_SIZE;
    m_buf.resize(bufferSize);
}

void SerialFrameParser::reset()
{
    m_head = 0;
    m_tail = 0;
    m_len = 0;
}

void SerialFrameParser::append(const QByteArray &data)
{
    if (data.isEmpty()) return;

    const int count = data.size();
    for (int i = 0; i < count; ++i) {
        if (m_len == m_buf.size()) {
            // overwrite oldest
            m_tail = (m_tail + 1) % m_buf.size();
            m_len--;
        }
        m_buf[m_head] = data[i];
        m_head = (m_head + 1) % m_buf.size();
        m_len++;
    }

    tryParseFrames();
}

void SerialFrameParser::tryParseFrames()
{
    while (m_len >= FRAME_SIZE) {
        const int idx = m_tail;

        // align to HEAD
        const quint8 b0 = static_cast<quint8>(m_buf[idx]);
        if (b0 != HEAD) {
            consume(1);
            continue;
        }

        quint8 frame[FRAME_SIZE];
        for (int i = 0; i < FRAME_SIZE; ++i) {
            frame[i] = static_cast<quint8>(m_buf[(idx + i) % m_buf.size()]);
        }

        // tail byte check
        if (frame[FRAME_SIZE - 1] != TAIL) {
            consume(1);
            continue;
        }

        // XOR check: xor bytes 0..16 equals frame[17]
        quint8 x = 0;
        for (int i = 0; i < 17; ++i) x ^= frame[i];
        if (x != frame[17]) {
            consume(1);
            continue;
        }

        const float raw = readFloatLE(&frame[1]);
        const float filtered = readFloatLE(&frame[5]);
        const float tofRaw = readFloatLE(&frame[9]);
        const float tofFilt = readFloatLE(&frame[13]);

        emit frameDecoded(raw, filtered, tofRaw, tofFilt);
        consume(FRAME_SIZE);
    }
}

void SerialFrameParser::consume(int n)
{
    if (n <= 0) return;
    m_tail = (m_tail + n) % m_buf.size();
    m_len -= n;
    if (m_len < 0) m_len = 0;
}

