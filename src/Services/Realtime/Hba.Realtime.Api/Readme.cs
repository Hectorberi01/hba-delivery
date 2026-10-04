namespace Hba.Realtime.Api;

/// <summary>
/// Le temps reel : ce service pousse aux applications ce qu'elles allaient
/// chercher en sondant.
/// </summary>
///
/// <remarks>
/// POURQUOI UN SEUL PROJET, ALORS QUE TOUS LES AUTRES SERVICES EN ONT QUATRE.
///
/// Un dossier Domain existe pour tenir des INVARIANTS dans le temps : une course ne
/// repasse pas de « terminee » a « en cours », un paiement ne se capture pas deux
/// fois, une grille ne se modifie pas — on en cree une version. Ce service n'a
/// aucun invariant de ce genre, parce qu'il ne possede RIEN. Il lit deux bus et
/// ecrit dans des sockets ; si on l'eteint une heure, il ne manque aucune donnee au
/// systeme, seulement du confort aux ecrans.
///
/// Un dossier Application existe pour porter des decisions metier testables sans
/// serveur. Les seules decisions d'ici sont : « cet abonne a-t-il le droit de
/// recevoir ce flux » — et la reponse n'est PAS calculee ici, elle est demandee au
/// service proprietaire — et « a qui renvoyer ce message », qui est du routage.
///
/// Creer les deux dossiers aurait donc produit deux projets vides, et un lecteur
/// aurait cherche la regle metier qui s'y cache. Il n'y en a pas, et c'est le
/// propos : ce service est un ADAPTATEUR, pas un domaine.
///
/// CE QU'IL NE FAUT PAS Y METTRE, DU COUP. Le jour ou quelqu'un voudra decider ici
/// quoi diffuser — « on n'envoie la position que si la course est en cours », « on
/// agrege les battements par cinq secondes » — la question est de savoir si c'est
/// une regle metier. Si oui, elle appartient au service proprietaire, pas ici. Si
/// c'est une regle de TRANSPORT, elle peut rester, et il faudra alors se demander
/// si ce projet unique tient toujours.
///
/// --------------------------------------------------------------------------------
///
/// CE SERVICE N'A NI BASE DE DONNEES NI INBOX, ET LES DEUX ABSENCES SONT RAISONNEES.
///
/// PAS DE BASE : il ne possede aucune donnee. Comme Routing et Location, mais pour
/// une raison encore plus simple — eux tiennent au moins un cache ou un index, lui
/// ne garde que des connexions ouvertes.
///
/// PAS D'INBOX, ET C'EST LE POINT LE PLUS CONTRE-INTUITIF. Tous les autres
/// consommateurs Kafka du depot passent par <c>KafkaConsumerBase</c>, qui ouvre une
/// Inbox avant de traiter : c'est ce qui empeche de crediter deux fois un grand
/// livre ou d'envoyer deux fois un SMS. Ici l'effet d'un message est d'ECRIRE UNE
/// TRAME DANS UNE SOCKET, et ecrire deux fois le meme etat est sans consequence :
/// l'ecran affiche la meme chose. L'Inbox aurait donc coute une base, une
/// transaction par message sur le chemin le plus chaud, et une table qui grossit
/// pour des faits dont aucun ne merite d'etre relu.
///
/// LA CONSEQUENCE A ACCEPTER : un message peut etre perdu. Si ce service tombe
/// pendant trente secondes, les changements d'etat de ces trente secondes ne seront
/// jamais pousses — le consommateur reprend au PLUS RECENT, pas au dernier offset
/// acquitte (voir <see cref="Messaging.DiffusionConsumerBase{TMessage}"/>). C'est pour cela
/// que LE SONDAGE RESTE EN PLACE dans les applications, ralenti a trente secondes :
/// le temps reel est un confort, le sondage est la verite. Supprimer le second
/// parce que le premier marche serait echanger un ecran lent contre un ecran faux.
/// </remarks>
internal static class Readme
{
}
