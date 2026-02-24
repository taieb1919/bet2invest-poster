Lance l'application bet2invest-poster pour tester avec Telegram.

Étapes :
1. Arrête toute instance existante de Bet2InvestPoster (cherche les processus dotnet contenant "Bet2InvestPoster" et tue-les)
2. Attends 2 secondes que le port Telegram polling soit libéré
3. Lance l'application en arrière-plan avec les variables d'environnement du fichier `.env` à la racine du projet. Utilise le script `app.run.sh` à la racine du projet.
4. Attends 8 secondes puis affiche les 15 dernières lignes de logs pour confirmer le démarrage
5. Indique à l'utilisateur que l'app tourne et qu'il peut envoyer des commandes Telegram (/run, /status)
