#ifndef MAINWINDOW_H
#define MAINWINDOW_H

#include <QMainWindow>
#include <QFrame>
#include <QStackedWidget>
#include <QPushButton>
#include <QLabel>
#include <QElapsedTimer>
#include <QTimer>

#include "modules/connection/connectionwidget.h"

class FilterDataWidget;
class CurveAnalysisWidget;
class SmartTuningWidget;
class SerialDataEngine;

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    MainWindow(QWidget *parent = nullptr);
    ~MainWindow();

private slots:
    void onConnectionClicked();
    void onFilterDataClicked();
    void onCurveAnalysisClicked();
    void onSmartTuningClicked();
    void onConnectRequested(const QString &portName, int baudRate, ConnectionWidget::TransportMode mode);
    void onDisconnectRequested();

private:
    void setupUi();
    void setupLeftPanel();
    void setupStackedWidget();
    void setActiveButton(QPushButton *active);

    QWidget *m_centralWidget;
    QFrame *m_leftPanel;
    QStackedWidget *m_stackedWidget;

    QPushButton *m_btnConnection;
    QPushButton *m_btnFilterData;
    QPushButton *m_btnCurveAnalysis;
    QPushButton *m_btnSmartTuning;

    QLabel *m_labelStatus;
    QLabel *m_labelRecvData;
    QLabel *m_labelRawBytes;
    QLabel *m_labelDataFreq;

    ConnectionWidget *m_connectionWidget;
    FilterDataWidget *m_filterDataWidget;
    CurveAnalysisWidget *m_curveAnalysisWidget;
    SmartTuningWidget *m_smartTuningWidget;

    // 智能调参时弹出的独立智能调参窗口
    SmartTuningWidget *m_smartTuningPopupWidget;

    SerialDataEngine *m_serialEngine;
    QTimer *m_freqUpdateTimer;
    qint64 m_recvCount;
    int m_freqCount;
    void updateStatusLabels();
};

#endif // MAINWINDOW_H
