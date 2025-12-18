# инстукция
заходим в кабинет клиента

## создание приложения
#### переходим сюда https://oauth.yandex.ru/
#### кнопка создать справа-сверху
#### выбираем для "для авторизации пользователей"
#### вводим имя, иконку
#### веб-сервисы, redirect url - https://oauth.yandex.ru/verification_code, sugget hostname - пустой
#### запрашиваемые права - direct:api
#### создать<br/><br/>

## приготовления к получению постоянного токена
#### ставим postman
#### создаем вкладку, в строке отправки выбираем post, вставляем ссылку для отправки - https://oauth.yandex.ru/token
#### ниже выбираем body, x-www-form-urlencoded
#### дальше ключ-значение: grant_type - authorization_code, client_id - ClientId созданного приложения, client_secret - Client secret созданного приложения, code - пока пустой<br/><br/>

## получение временного и постоянного токена
#### переходим сюда вставляя CLIENT_ID - https://oauth.yandex.ru/authorize?response_type=code&client_id=<CLIENT_ID_созданного_приложения>
#### на экране временный токен, вставляем его в code в postman и отправляем
#### в ответе получаем access_token, это постоянный токен, ***его надо обновлять раз в год***
#### если 
