# Installation de l'environnement de développement

1. Installer Unity (testé avec la version 2017.2.0f3)
2. Installer le SDK Java (JDK)
3. Ouvrir le projet dans Unity
4. Importer le paquetage Facebook_SDK qui se trouve dans le repo : `Assets` > `Import Package` > `Custom Package`
5. Quitter Unity
6. Installer Android Studio
7. Ouvrir le SDK Manager
8. Dans l'onglet Android SDK, cocher la case `Show Package Details`
9. Dans la liste cocher exactement 
   - `Android SDK Platform 25`
   - `Android SDK Platform 23`
   - `Android SDK Platform 22`
10. Relancer Unity
11. Dans `File` > `Build Settings` sélectionner `Android`
12. Dans `Facebook` > `Edit Settings`,  renseigner les champs `App Id`et `Client token` avec les informations de la page développeur de Facebook
13. Renseigner les informations de la section `Android Build Facebook Settings `dans la page développeur de Facebook