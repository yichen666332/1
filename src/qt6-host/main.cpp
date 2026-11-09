#include "mainwindow.h"
#include <QApplication>
#include <QFont>

int main(int argc, char *argv[])
{
    QApplication a(argc, argv);
    // 全局默认字体略放大，便于阅读（未单独写 stylesheet 的控件会继承）
    QFont appFont = a.font();
    if (appFont.pointSize() > 0)
        appFont.setPointSize(appFont.pointSize() + 1);
    else if (appFont.pixelSize() > 0)
        appFont.setPixelSize(appFont.pixelSize() + 1);
    else
        appFont.setPointSize(10);
    a.setFont(appFont);

    MainWindow w;
    w.show();
    return a.exec();
}
