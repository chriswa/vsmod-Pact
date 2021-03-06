using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Pact {
  public class PactPlayerEntityBehavior : EntityBehavior {
    private readonly PactMod pactMod;
    public PactPlayerEntityBehavior(Entity entity, PactMod pactMod) : base(entity) {
      this.pactMod = pactMod;
    }
    public override string PropertyName() {
      return "PactPlayerEntityBehavior";
    }
    public override void OnEntityRevive() {
      // be extra super careful to avoid crashes, even though this should never happen due to business logic
      if (this.entity.World.Side != EnumAppSide.Server || !(this.entity is EntityPlayer)) {
        this.entity.World.Api.Logger.Debug("PactPlayerEntityBehavior: unexpected: client side or entity not player, skipping");
        return;
      }

      // get IServerPlayer from Entity, per https://github.com/anegostudios/vssurvivalmod/blob/master/Item/ItemTemporalGear.cs#L182
      IServerPlayer player = this.entity.World.PlayerByUid((this.entity as EntityPlayer).PlayerUID) as IServerPlayer;

      // allow pacts to respond to players reviving
      pactMod.OnPlayerRevive(player);
    }
  }
}