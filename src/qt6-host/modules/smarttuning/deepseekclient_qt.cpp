#include "deepseekclient_qt.h"

#include <QCoreApplication>
#include <QFile>
#include <QTextStream>
#include <QJsonDocument>
#include <QJsonArray>
#include <QNetworkRequest>
#include <QNetworkReply>
#include <QNetworkProxy>
#include <QNetworkProxyFactory>

DeepSeekClientQt::DeepSeekClientQt(QObject *parent)
    : QObject(parent)
    , m_manager(this)
    , m_model(QString::fromUtf8("deepseek-chat"))
{
    // 全局禁用系统代理，避免 407 "Host requires authentication"
    QNetworkProxyFactory::setUseSystemConfiguration(false);

    QNetworkProxy proxy;
    proxy.setType(QNetworkProxy::NoProxy);
    QNetworkProxy::setApplicationProxy(proxy);
    m_manager.setProxy(proxy);
}

void DeepSeekClientQt::setApiKey(const QString &key)
{
    m_apiKey = key.trimmed();
}

void DeepSeekClientQt::setModel(const QString &model)
{
    if (!model.trimmed().isEmpty())
        m_model = model.trimmed();
}

bool DeepSeekClientQt::loadApiKeyFromFiles(const QStringList &paths, QString *errorOut)
{
    for (const QString &path : paths) {
    QFile f(path);
    if (!f.exists())
        continue;
    if (!f.open(QIODevice::ReadOnly | QIODevice::Text))
        continue;
    QTextStream ts(&f); // Qt 6 默认使用 UTF-8，无需 setCodec
    const QString all = ts.readAll();
    f.close();

        // 优先从 "Bearer xxx" 提取
        int idx = all.indexOf("Bearer ");
        QString key;
        if (idx >= 0) {
        int start = idx + 7;
        int end = start;
        while (end < all.length()) {
            const QChar ch = all.at(end);
            if (ch.isSpace() || ch == '\'' || ch == '\"' || ch == '\r' || ch == '\n')
                break;
            ++end;
        }
        key = all.mid(start, end - start).trimmed();
        } else {
            // 否则按行分割，取第一行非空、非注释行
            QString current;
            for (int i = 0; i < all.length(); ++i) {
                const QChar ch = all.at(i);
                if (ch == '\r' || ch == '\n') {
                    const QString s = current.trimmed();
                    if (!s.isEmpty() && !s.startsWith('#')) {
                        key = s;
                        break;
                    }
                    current.clear();
                } else {
                    current.append(ch);
                }
            }
            if (key.isEmpty()) {
                const QString s = current.trimmed();
                if (!s.isEmpty() && !s.startsWith('#'))
                    key = s;
            }
        }

        if (!key.isEmpty()) {
            setApiKey(key);
            if (errorOut) *errorOut = QString();
            return true;
        }
    }

    if (errorOut) {
        *errorOut = QString::fromUtf8("未能在 Deepseek.txt 中找到有效的 API Key。");
    }
    return false;
}

void DeepSeekClientQt::postChat(const QJsonObject &body,
                                std::function<void(bool ok, const QJsonObject &root, const QString &err)> callback)
{
    if (m_apiKey.isEmpty()) {
        callback(false, QJsonObject(), QString::fromUtf8("API Key 未配置。"));
        return;
    }

    QNetworkRequest req(QUrl(QString::fromUtf8("https://api.deepseek.com/v1/chat/completions")));
    req.setHeader(QNetworkRequest::ContentTypeHeader, QString::fromUtf8("application/json"));
    req.setRawHeader("Accept", "application/json");
    req.setRawHeader("Authorization", QByteArray("Bearer ") + m_apiKey.toUtf8());

    const QJsonDocument doc(body);
    QByteArray payload = doc.toJson(QJsonDocument::Compact);

    QNetworkReply *reply = m_manager.post(req, payload);
    QObject::connect(reply, &QNetworkReply::finished, this, [reply, callback]() {
        reply->deleteLater();

        if (reply->error() != QNetworkReply::NoError) {
            callback(false, QJsonObject(),
                     QString::fromUtf8("网络错误: %1").arg(reply->errorString()));
            return;
        }

        const QByteArray raw = reply->readAll();
        QJsonParseError err;
        const QJsonDocument respDoc = QJsonDocument::fromJson(raw, &err);
        if (err.error != QJsonParseError::NoError || !respDoc.isObject()) {
            callback(false, QJsonObject(),
                     QString::fromUtf8("解析响应失败: %1").arg(QString::fromUtf8(raw)));
            return;
        }

        callback(true, respDoc.object(), QString());
    });
}

void DeepSeekClientQt::quickChat(const QString &prompt,
                                 std::function<void(bool ok, const QString &result)> callback)
{
    QJsonObject body;
    body["model"] = m_model;
    body["temperature"] = 0.2;

    QJsonArray messages;
    messages.append(QJsonObject{
                        {"role", "system"},
                        {"content", QString::fromUtf8("You are a helpful assistant.")}
                    });
    messages.append(QJsonObject{
                        {"role", "user"},
                        {"content", prompt}
                    });
    body["messages"] = messages;

    postChat(body, [callback](bool ok, const QJsonObject &root, const QString &err) {
        if (!ok) {
            callback(false, err);
            return;
        }
        const QJsonArray choices = root.value("choices").toArray();
        if (choices.isEmpty()) {
            callback(false, QString::fromUtf8("响应中没有 choices 字段。"));
            return;
        }
        const QJsonObject first = choices.at(0).toObject();
        const QJsonObject msg = first.value("message").toObject();
        const QString content = msg.value("content").toString();
        callback(true, content);
    });
}

void DeepSeekClientQt::getTuningAsync(const QString &systemPrompt,
                                      const QString &userPrompt,
                                      std::function<void(bool ok, const QJsonObject &json, const QString &err)> callback)
{
    QJsonObject body;
    body["model"] = m_model;
    body["temperature"] = 0.2;

    QJsonArray messages;
    messages.append(QJsonObject{
                        {"role", "system"},
                        {"content", systemPrompt}
                    });
    messages.append(QJsonObject{
                        {"role", "user"},
                        {"content", userPrompt}
                    });
    body["messages"] = messages;

    postChat(body, [callback](bool ok, const QJsonObject &root, const QString &err) {
        if (!ok) {
            callback(false, QJsonObject(), err);
            return;
        }
        const QJsonArray choices = root.value("choices").toArray();
        if (choices.isEmpty()) {
            callback(false, QJsonObject(), QString::fromUtf8("响应中没有 choices 字段。"));
            return;
        }
        const QJsonObject first = choices.at(0).toObject();
        const QJsonObject msg = first.value("message").toObject();
        const QString contentRaw = msg.value("content").toString();
        QString content = contentRaw.trimmed();

        // 1) 去掉 ```json ... ``` 代码块包裹
        if (content.startsWith("```")) {
            int firstNl = content.indexOf('\n');
            if (firstNl >= 0)
                content = content.mid(firstNl + 1);
            if (content.endsWith("```"))
                content.chop(3);
            content = content.trimmed();
        }

        // 2) 提取第一个 '{' 到最后一个 '}' 之间的子串
        int l = content.indexOf('{');
        int r = content.lastIndexOf('}');
        QString jsonText;
        if (l >= 0 && r > l) {
            jsonText = content.mid(l, r - l + 1);
        } else {
            jsonText = content;
        }

        QJsonParseError perr;
        const QJsonDocument jd = QJsonDocument::fromJson(jsonText.toUtf8(), &perr);
        if (perr.error != QJsonParseError::NoError || !jd.isObject()) {
            callback(false, QJsonObject(),
                     QString::fromUtf8("LLM 返回内容不是合法 JSON: %1").arg(contentRaw));
            return;
        }
        callback(true, jd.object(), QString());
    });
}

void DeepSeekClientQt::ping(std::function<void(bool ok, const QString &detail)> callback)
{
    QJsonObject body;
    body["model"] = m_model;
    body["temperature"] = 0.0;
    body["max_tokens"] = 16;

    QJsonArray messages;
    messages.append(QJsonObject{
                        {"role", "system"},
                        {"content", QString::fromUtf8("You are a helpful assistant. Only output JSON. No extra text.")}
                    });
    messages.append(QJsonObject{
                        {"role", "user"},
                        {"content", QString::fromUtf8("Return exactly this JSON: {\"ok\": true}")}
                    });
    body["messages"] = messages;

    postChat(body, [callback](bool ok, const QJsonObject &root, const QString &err) {
        if (!ok) {
            callback(false, err);
            return;
        }
        // 期望模型直接返回 {"ok":true}
        if (root.contains("ok")) {
            const bool okField = root.value("ok").toBool(false);
            callback(okField, okField ? QString::fromUtf8("ok") : QString::fromUtf8("字段 ok 不是 true"));
            return;
        }
        callback(false, QString::fromUtf8("未在响应中找到 ok 字段。"));
    });
}

