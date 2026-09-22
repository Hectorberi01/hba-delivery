// Le type Delivery et le namespace Hba.Delivery porteraient le même nom court :
// l'alias lève l'ambiguïté une fois pour toutes, dans tout le projet.
global using DeliveryAggregate = Hba.Delivery.Domain.Deliveries.Delivery;
