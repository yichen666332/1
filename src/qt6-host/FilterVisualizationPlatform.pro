QT       += core gui network

greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

CONFIG += c++17

TARGET = FilterVisualizationPlatform
TEMPLATE = app

# 不使用 .ui 文件，纯代码实现
SOURCES += \
    main.cpp \
    mainwindow.cpp \
    core/serialdataengine.cpp \
    core/serialframeparser.cpp \
    core/winserialreader.cpp \
    modules/connection/connectionwidget.cpp \
    modules/filterdata/filterdatawidget.cpp \
    modules/curveanalysis/curveanalysiswidget.cpp \
    modules/curveanalysis/simplechartwidget.cpp \
    modules/curveanalysis/combined/combinedfilterwidget.cpp \
    modules/smarttuning/smarttuningwidget.cpp \
    modules/smarttuning/deepseekclient_qt.cpp \
    modules/smarttuning/akf/akf_autotuner_qt.cpp \
    modules/smarttuning/oef/oneeuro_autotuner_qt.cpp \
    modules/smarttuning/rhf/robusthinf_autotuner_qt.cpp

HEADERS += \
    mainwindow.h \
    core/filterdatapacket.h \
    core/serialdataengine.h \
    core/serialframeparser.h \
    core/winserialreader.h \
    core/filters/kalman1d.h \
    core/filters/medianfilter.h \
    core/filters/hinffilter1d.h \
    core/filters/adaptivekalmanfilter.h \
    core/filters/oneeurolikefilter.h \
    core/filters/robusthinfinityfilter.h \
    modules/filterdata/oneeurolikefilter_qt.h \
    modules/filterdata/medianfilter_qt.h \
    modules/filterdata/robusthinfinityfilter_qt.h \
    modules/connection/connectionwidget.h \
    modules/filterdata/filterdatawidget.h \
    modules/curveanalysis/curveanalysiswidget.h \
    modules/curveanalysis/eval/evalwindow_qt.h \
    modules/curveanalysis/oef/oefscorer_qt.h \
    modules/curveanalysis/rhf/rhfscorer_qt.h \
    modules/curveanalysis/simplechartwidget.h \
    modules/curveanalysis/combined/combinedfilterwidget.h \
    modules/smarttuning/smarttuningwidget.h \
    modules/smarttuning/deepseekclient_qt.h \
    modules/smarttuning/akf/akf_autotuner_qt.h \
    modules/smarttuning/oef/oneeuro_autotuner_qt.h \
    modules/smarttuning/rhf/robusthinf_autotuner_qt.h

# Default rules for deployment.
qnx: target.path = /tmp/$${TARGET}/bin
else: unix:!android: target.path = /opt/$${TARGET}/bin
!isEmpty(target.path): INSTALLS += target
