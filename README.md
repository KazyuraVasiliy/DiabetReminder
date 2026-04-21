# DiabetReminder

Сервис осуществляет мониторинг показателей и отправляет уведомление в выбранный канал (Telegram, Email).
- уровень глюкозы (гипогликемия, гипергликимия, резкий рост или падение на основе заданной дельты);
- уровень заряда устройств;
- заполненность MongoDB;
- статус оплаты и состояния хостинга (реализовано на примере RUVDS).

## Сборка

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

mkdir -p /opt/diabet_reminder
dotnet publish ./DiabetReminder/DiabetReminder.csproj -c Release -r linux-x64 --self-contained true -o /opt/diabet_reminder
```

## Конфигурация

Шаблон конфигурации находится в файле secrets.template.

Все задержки указаны в миллисекундах; каждая секция может быть пропущена.

### Telegram
Секция предусматривает Token BotFather и идентификатор группы или канала.
```json
"Telegram": {
  "Token": "0000000000:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
  "ChatId": -1000000000000
}
```

### Smtp
Секция предусматривает стандартные настройки SMTP; параметр To принимает массив адресов на которые будет отправлено уведомление.
```json
"Smtp": {
  "Server": "smtp.server.ru",
  "Port": 465,
  "Username": "username@server.ru",
  "Password": "password",
  "To": [ "user@server.ru" ]
}
```

### Nightscout

- Подсекция Glucose принимает нестрогие границы уровня глюкозы в ммол/л;
- подсекция Mongo принимает непосредственно строку подключения до MongoDb, а максимальный размер базы выставляется вручную (по умолчанию указан размер для бесплатного экземпляра);
- подсекция Google принимает идентификатор Google Календаря (должна быть заполнена секция Google) для создания события с текущим уровнем глюкозы (удобно для носимых устройств, которые не поддерживают вывод пользовательских данных, но поддерживают календарь);
- подсекция Battery принимает наименования устройств (передаваемых в Nightscout) для отслеживания уровня заряда;
- подсекции Users и Channels принимают соответсвенно список пользователей, которых необходимо тегнуть (если такая возможность доступна в рамках канала) и список каналов уведомлений по каждому типу уведомления.

```json
"Nightscout": {
  "Uri": "https://mynightscout.ru/api/v1/",
  "ApiSecret": "api-secret",
  "Parameters": {
    "Glucose": {
      "Hypoglycemia": 3.9,
      "Hyperglycemia": 9,
      "Delta": 0.3,
      "HighGlucose": 7.9,
      "LowGlucose": 4.5
    },
    "Mongo": {
      "ConnectionString": "ConnectionString",
      "DatabaseName": "Database",
      "MaxDatabaseSizeMib": 496,
      "WarningPercent": 80,
      "Delay": 86400000
    },
    "Google": {
      "CalendarId": "123456789@group.calendar.google.com",
      "Delay": 60000
    },
    "Delay": {
      "Error": 600000,
      "Warning": 300000,
      "Default": 60000
    },
    "Battery": {
      "Devices": [ "Bubble", "openaps://Phone", "Phone" ],
      "Delay": 3600000,
      "WarningPercent": 10
    },
    "Users": {
      "Error": null,
      "Hypoglycemia": [ "@User1" ],
      "Hyperglycemia": [ "@User1", "@User2" ],
      "HighGlucose": null,
      "LowGlucose": [],
      "Mongo": [],
      "Battery": []
    },
    "Channels": {
      "Error": [ "Telegram" ],
      "Hypoglycemia": [ "Telegram", "Email" ],
      "Hyperglycemia": [ "Telegram" ],
      "HighGlucose": null,
      "LowGlucose": [],
      "Mongo": [],
      "Battery": []
    }
  }
}
```

### Ruvds
Секция предусматривает Token и идентификатор сервера; подсекции Users и Channels по смыслу соответствуют Nightscout.
```json
"RUVDS": {
  "Uri": "https://api.ruvds.com/v2",
  "Token": "token",
  "Parameters": {
    "ServerId": 0,
    "Delay": {
      "Paid": 86400000,
      "Status": 1200000
    },
    "Users": {
      "Status": [],
      "Paid": [ "@User1" ]
    },
    "Channels": {
      "Status": [ "Email" ],
      "Paid": [ "Telegram" ]
    }
  }
}
```

### Google
Секция предусматривает параметры для работы с Google API
```json
"Google": {
  "ServiceAccount": {
    "type": "service_account",
    "project_id": "project_id",
    "private_key_id": "private_key_id",
    "private_key": "private_key",
    "client_email": "client_email",
    "client_id": "client_id",
    "auth_uri": "https://accounts.google.com/o/oauth2/auth",
    "token_uri": "https://oauth2.googleapis.com/token",
    "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
    "client_x509_cert_url": "client_x509_cert_url",
    "universe_domain": "googleapis.com"
  }
}
```
