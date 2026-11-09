#ifndef DEEPSEEKCLIENT_QT_H
#define DEEPSEEKCLIENT_QT_H

#include <QObject>
#include <QNetworkAccessManager>
#include <QJsonObject>
#include <functional>

class DeepSeekClientQt : public QObject
{
    Q_OBJECT

public:
    explicit DeepSeekClientQt(QObject *parent = nullptr);

    // 从若干候选路径中读取 API Key（首个成功的路径即为结果）
    // 返回 true 表示成功；errorOut 提供错误说明。
    bool loadApiKeyFromFiles(const QStringList &paths, QString *errorOut = nullptr);
    void setApiKey(const QString &key);
    void setModel(const QString &model);

    // 简单对话：调用 /chat/completions，返回模型文本
    void quickChat(const QString &prompt,
                   std::function<void(bool ok, const QString &result)> callback);

    // 调参专用：给定 system / user prompt，要求模型只返回 JSON。
    // 回调里返回解析后的 JSON 对象。
    void getTuningAsync(const QString &systemPrompt,
                        const QString &userPrompt,
                        std::function<void(bool ok, const QJsonObject &json, const QString &err)> callback);

    // Ping：让模型返回 {"ok":true}
    void ping(std::function<void(bool ok, const QString &detail)> callback);

private:
    void postChat(const QJsonObject &body,
                  std::function<void(bool ok, const QJsonObject &root, const QString &err)> callback);

    QNetworkAccessManager m_manager;
    QString m_apiKey;
    QString m_model;
};

#endif // DEEPSEEKCLIENT_QT_H

