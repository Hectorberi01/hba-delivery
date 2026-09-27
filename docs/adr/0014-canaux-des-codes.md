# 0014 — WhatsApp porte le code de connexion, le SMS reste seul vers le destinataire

Statut : **proposée** — septembre 2026. En attente de décision.

Tranche le point 10 de `architecture/points-a-trancher.md`.

## Contexte

Le service Notification était écrit sans adaptateur réel : `Sms:Provider`
valait `none` et chaque envoi était consigné en « ignoré ». Personne ne pouvait
donc se connecter, l'OTP étant le seul moyen d'authentification du client et du
livreur.

Les prix, pour trois messages par course :

| Voie | Par message | Par course |
|---|---|---|
| WhatsApp, gabarit *authentication*, Bénin | ≈ 2,3 F | ≈ 7 F |
| SMS, accord direct MTN / Moov | 10–25 F | 30–75 F |
| SMS, agrégateur international | ≈ 169 F | ≈ 507 F |

Un pilote à vingt courses par jour coûterait environ 300 000 F par mois chez un
agrégateur international. Le prix n'est donc pas un détail de fin de projet.

Mais **deux codes coexistent, et ils ne s'adressent pas au même public.**

`otp_login` part vers le titulaire d'un compte : il s'inscrit, donc il peut
donner son consentement. `delivery_otp` part vers le **destinataire**, qui n'a
pas de compte HBA, n'a rien signé, et que l'expéditeur désigne par un simple
numéro de téléphone. Or Meta impose un **opt-in préalable** à tout message de
gabarit.

## Décision

**`otp_login` part par WhatsApp, avec le SMS en repli.** Le catalogue porte la
chaîne — WhatsApp d'abord, SMS ensuite — et le service bascule seul quand le
premier canal n'est pas configuré ou échoue. Un code de connexion qui ne part
pas laisse l'utilisateur dehors : le repli n'est pas un luxe.

**Demander WhatsApp, c'est demander la chaîne, pas WhatsApp seul.** Un appelant
qui impose explicitement un canal renonce au repli ; c'est ce qui permet de
forcer le SMS là où lui seul est acceptable.

**`delivery_otp` part par SMS, et uniquement par SMS.** WhatsApp est fermé pour
le destinataire, quel que soit son prix. Un test le vérifie, pour qu'on ne
« complète » pas le catalogue un jour par inadvertance.

**Tant qu'aucun opérateur SMS n'est branché, le code de remise est communiqué à
l'expéditeur**, dans son écran de suivi, et c'est lui qui le transmet au
destinataire par le moyen qu'il veut. Disposition provisoire, à retirer dès
qu'un opérateur existe.

**L'option du courriel est abandonnée.** Elle avait été envisagée comme
solution d'attente pour la connexion ; WhatsApp la rend inutile, et un livreur
ne relève pas sa boîte entre deux courses.

## Conséquences

- **Le texte du message d'authentification n'est plus le nôtre.** Meta fixe le
  corps d'un modèle d'authentification — « {{1}} is your verification code »,
  qu'elle traduit selon le code de langue — et interdit les URL, les médias et
  les émojis. On choisit la langue et on fournit le code ; c'est tout. Le texte
  français soigneusement écrit pour le SMS ne sert plus que de repli.

- **Le code voyage deux fois** dans la charge utile, dans le corps et dans le
  bouton de copie. C'est le format attendu par l'API, pas une redite.

- **Une démarche préalable, de durée non maîtrisée** : vérification du compte
  Meta Business, création et approbation du modèle `hba_otp_login` en français.
  Rien ne part avant cette approbation.

- **L'opt-in devient une donnée du compte.** Identity doit le recueillir à
  l'inscription et le conserver ; Notification ne le vérifie pas, c'est
  l'appelant qui ne demande WhatsApp que pour un titulaire consentant. CE POINT
  N'EST PAS ENCORE IMPLEMENTE et reste ouvert.

- **Un opérateur SMS reste indispensable**, et le point 10 ne disparaît pas
  pour autant : le destinataire n'a pas d'autre canal. Le volume, lui, est
  divisé par trois, ce qui change les termes de la négociation.

- **Une trace par tentative.** Quand WhatsApp échoue et que le SMS prend le
  relais, deux lignes sont écrites dans `sent_notifications`. C'est ce qui
  permettra de savoir ce que le repli coûte réellement.

- **La version de l'API Graph est une donnée de configuration**, pas une
  constante du code : Meta retire chaque version environ deux ans après sa
  sortie, et une montée de version ne doit pas demander une livraison.

- **Un délai de dix secondes** sur l'appel WhatsApp, au-delà duquel on bascule
  sur le SMS. Un code qui met trente secondes à arriver a déjà échoué.

## Ce qui a été écarté

- **WhatsApp pour le code de remise** : impossible, faute d'opt-in du
  destinataire.
- **Le courriel** : inutile une fois WhatsApp en place, et inadapté au livreur.
- **Le SMS seul** : dix fois plus cher là où WhatsApp est utilisable.
- **WhatsApp sans repli** : une panne de Meta enfermerait tout le monde dehors.
