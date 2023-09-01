# SynthBorg

![Screenshot_2](https://github.com/Dark-V/SynthBorg/assets/58254635/233a8b89-97dc-43a9-8b3b-ab39a68d17ba)

Доступные команды: <br/>
/attach - Изменить клавишу хоткея пропуска tts озвучек  <br/>
/clear - Очистить окно вывода  <br/>
/ttv_info - Включает или выключает подробный вывод TTV  <br/>
/save - Сохранить текущие настройки конфигурации  <br/>
/reload - Перезагрузить подключение к TTV  <br/>
/say [сообщение] - Произнести указанное сообщение внутри приложения  <br/>
/blocklist [add/del/show] [имя_пользователя] Добавить/удалить/показать список пользователь игнор списка  <br/>
/whitelist [add/del/show] [имя_пользователя] - Добавить/удалить/показать список пользователь белого списка  <br/>
/me [allow/deny] \"текст_сообщения\" - Изменить текст сообщения для команды !me <br/>

## RU:
- Это программа позволяет читать чат от вашего аккаунта, используя систему авторизации TTV.
- Если пользователь напишет в чат - !say TEXT, то программа проверит разрешено ли пользователю использовать команду, если у пользователя достаточно привилегий - то будет озвучена фраза из его сообщения.

- Привилегии доступа настраиваются внутри программы.
- Программа имеет в себе настраиваемый белый и черный список пользователей.
- Программа имеет возможность добавить список заблокированных слов, используя файл "C:\Users\\%username%\Documents\SynthBorg\ignoredWords.txt".
- Например имея в файле список слов, сообщение  **"wakan is loz"** будет озвучено как  **"\* is \*"**: <br/>
  wakan <br/>
  loz <br/>
  
## Запуск:
1. Скачайте программу, положите exe файл в удобное вам место, например рабочий стол.
2. Откройте программу, в поле "Channel" укажите свой ник канала.
3. Нажмите на кнопку GEN, возле Token поля, вам будут выведенен текст, ЧИТАЙТЕ что там написанно!
4. Установите уровни доступа в Permissions. (Moderators,Vips,Subs)
5. Напишите команды /save & /reload в поле ввода снизу.
6. Добавьте людей в белый/черный список. (более подробно в /help).
