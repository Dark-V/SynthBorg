# SynthBorg

![Screenshot_2](https://github.com/Dark-V/SynthBorg/assets/58254635/233a8b89-97dc-43a9-8b3b-ab39a68d17ba)

Доступные команды: <br/>
/clear - Очистить окно вывода <br/>
/ttv_info - Включает или выключает подробный вывод TTV <br/>
/attach - Перепривязка клавишы на STOP ALL (shift + re-attached btn) <br/>
/testit - Произнести тестовое сообщение <br/>
/reload - Перезагрузить подклбчение к TTV <br/>
/say [сообщение] - Произнести указанное сообщение <br/>
/ignore [имя_пользователя] - Игнорировать указанного пользователя <br/>
/pardon [имя_пользователя] - Удалить пользователя из списка игнорируемых <br/>
/blocklist - Показать текущий список заблокированных пользователей <br/>
/whitelist [имя_пользователя] - Добавить указанного пользователя в белый список <br/>
/unwhitelist [имя_пользователя] - Удалить указанного пользователя из белого списка <br/>
/save - Сохранить текущие настройки конфигурации <br/>
/me [allow/deny] \"текст_сообщения\" - Изменить текст сообщения для команды !me <br/>
/allowlist - Показать текущий список разрешенных пользователей <br/>

## RU:
- Это программа позволяет читать чат от вашего аккаунта, используя систему авторизации TTV.
- Если пользователь напишет в чат - !say TEXT, то программа проверит разрешено ли пользователю использовать команду, если у пользователя достаточно привилегий - то будет озвучена фраза из его сообщения.

- Привилегии доступа настраиваются внутри программы.
- Программа имеет в себе настраиваемый белый и черный список пользователей.
- Программа имеет возможность добавить список заблокированных слов, используя файл "C:\Users\\%username%\Documents\SynthBorg\ignoredWords.txt".
- Например имея в файле список слов, сообщение  **"wakan is loz"** будет озвучено как  **"\* is \*"**: <br/>
  wakan <br/>
  loz <br/>

## EN:
- This program allows read chat using your account from TTV. Made by using TTV authorization system.
- If user write to chat - !say TEXT, then the program will check if the user is allowed to use the command, if the user has enough privileges, then phrase will be announced using TTS.

- Access privileges are configured within the program.
- Program has a customizable white and black list for users.
- Program has the ability to add a list of blocked words using the file "C:\Users\\%username%\Documents\SynthBorg\ignoredWords.txt".
- For example, having a list of words in the file, the message 	**"wakan is loz"** will be voiced as 	**"\* is \*"**: <br/>
   wakan <br/>
   loz <br/>
