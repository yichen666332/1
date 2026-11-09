#ifndef CONNECTIONWIDGET_H
#define CONNECTIONWIDGET_H

#include <QWidget>
#include <QComboBox>
#include <QPushButton>
#include <QLabel>

class ConnectionWidget : public QWidget
{
    Q_OBJECT

public:
    enum class TransportMode {
        UsbToTtl,
        DataRadio
    };

    explicit ConnectionWidget(QWidget *parent = nullptr);
    ~ConnectionWidget();

    bool isConnected() const { return m_connected; }
    QString selectedPortName() const;
    int selectedBaudRate() const;
    TransportMode selectedTransportMode() const;
    void setConnected(bool connected);

signals:
    void connectRequested(const QString &portName, int baudRate, ConnectionWidget::TransportMode mode);
    void disconnectRequested();
    void refreshRequested();

private slots:
    void onConnectClicked();
    void onRefreshClicked();
    void onTransportModeChanged();
    void onProbeBaudClicked();

private:
    void setupUi();
    void fillAvailablePorts();
    void syncBaudToTransportMode();

    QComboBox *m_comboPort;
    QComboBox *m_comboBaudRate;
    QComboBox *m_comboTransportMode;
    QPushButton *m_btnConnect;
    QPushButton *m_btnRefresh;
    QPushButton *m_btnProbeBaud;
    QLabel *m_iconLabel;
    bool m_connected;
};

#endif // CONNECTIONWIDGET_H
