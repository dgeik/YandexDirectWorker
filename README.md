# YandexDirectWorker
заходим в кабинет клиента

## создание приложения
#### переходим сюда https://oauth.yandex.ru/
#### кнопка создать справа-сверху
#### выбираем для "для авторизации пользователей"
#### вводим имя, иконку
#### веб-сервисы, redirect url - https://oauth.yandex.ru/verification_code, sugget hostname - пустой
#### запрашиваемые права - direct:api
#### создать

## приготовления к получению постоянного токена
#### ставим postman
#### создаем вкладку, в строке отправки выбираем post, вставляем ссылку для отправки - https://oauth.yandex.ru/token
#### ниже выбираем body, x-www-form-urlencoded
#### дальше ключ-значение: grant_type - authorization_code, client_id - ClientId созданного приложения, client_secret - Client secret созданного приложения, code - пока пустой

## получение временного и постоянного токена
#### https://oauth.yandex.ru/authorize?response_type=code&client_id=<CLIENT_ID_созданного_приложения>
