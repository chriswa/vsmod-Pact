using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Config;

[assembly: ModInfo("Pact",
  Description = "A mod for Vintage Story which allows two players to respawn together in Wilderness Survival",
  Website = "https://github.com/chriswa/vsmod-Pact",
  Authors = new[] { "goxmeor" })]

namespace Pact {
  public class PactMod : ModSystem {
    private const string COMMAND_NAME = "pact";
    private const string COMMAND_DESC = "Kills the player typing the command, clears their spawn point, and respawns them with their new pact mate";
    private const string COMMAND_SYNTAX = "/pact PactMateName";
    private const string MESSAGE_COMMAND_MISSING_ARGS = "You must specify a pact mate by name: /pact PactMateName";
    private const string MESSAGE_COMMAND_TOO_MANY_ARGS = "You may not specify more than one pact mate.";
    private const string MESSAGE_COMMAND_SELF_PACT = "You may not form a pact with yourself.";
    private const string MESSAGE_COMMAND_PACT_OFFER_MADE = "Pact offered. {0} must now type /pact {1}";
    private const string MESSAGE_COMMAND_PACT_OFFER_RECEIVED = "{0} has offered a pact. To accept, type: /pact {0}\nIf you accept, you will lose your spawn point, die, and respawn beside ${0}.";
    private const string MESSAGE_PLAYER_NOT_FOUND = "Could not find a player online with the specified name.";
    private const string MESSAGE_PACT_OFFER_EXPIRED = "Your pact offer with {0} has expired.";
    private const string MESSAGE_PACT_START = "The pact has been made.";
    private const string MESSAGE_PACT_STEP_FOR_ALIVE_PLAYER = "{0} will join you soon.";
    private const string MESSAGE_PACT_STEP_FOR_STILL_DEAD_PLAYER = "{0} has respawned and awaits you.";
    private const string MESSAGE_BROADCAST_PACT_COMPLETE = "{0} and {1} have formed a pact.";

    public override void Start(ICoreAPI api) {
    }

    public override void StartClientSide(ICoreClientAPI api) {
    }

    private ICoreServerAPI sapi;

    private void LogEvent(string message) {
      sapi.Logger.Event("[Pact] [" + DateTime.Now.ToString("yyyy-MM-dd H:mm:ss") + "] " + message);
    }

    public override void StartServerSide(ICoreServerAPI sapi) {
      this.sapi = sapi;

      LogEvent("Hello Pact!");

      // register `/pact` command
      sapi.RegisterCommand(COMMAND_NAME, Lang.Get(COMMAND_DESC), Lang.Get(COMMAND_SYNTAX), (IServerPlayer player, int groupId, CmdArgs args) => {
        if (args.Length < 1) {
          SendChatMessage(player, MESSAGE_COMMAND_MISSING_ARGS);
        }
        else if (args.Length > 1) {
          SendChatMessage(player, MESSAGE_COMMAND_TOO_MANY_ARGS);
        }
        else {
          string specifiedPactMateName = args[0];
          foreach (IServerPlayer onlinePlayer in sapi.World.AllOnlinePlayers) {
            if (onlinePlayer.PlayerName.Equals(specifiedPactMateName)) {
              if (onlinePlayer.Equals(player)) {
                SendChatMessage(player, MESSAGE_COMMAND_SELF_PACT);
                return;
              }
              OfferPact(player, onlinePlayer);
              SendChatMessage(player, MESSAGE_COMMAND_PACT_OFFER_MADE, onlinePlayer.PlayerName, player.PlayerName);
              SendChatMessage(onlinePlayer, MESSAGE_COMMAND_PACT_OFFER_RECEIVED, player.PlayerName);
              return;
            }
          }
          SendChatMessage(player, MESSAGE_PLAYER_NOT_FOUND);
        }
      });

      // hook player respawn events
      sapi.Event.OnEntitySpawn += (Entity entity) => { // OnEntitySpawn fires when player joins, not when player respawns
        if (entity is EntityPlayer) {
          entity.AddBehavior(new PactPlayerEntityBehavior(entity, this));
          IServerPlayer player = entity.World.PlayerByUid((entity as EntityPlayer).PlayerUID) as IServerPlayer;
        }
      };

    }

    private HashSet<string> activePactOffers = new HashSet<string>();

    private int OFFER_EXPIRE_TIME_MS = 1000 * 60;

    private void OfferPact(IServerPlayer invokingPlayer, IServerPlayer targetPlayer) {
      string theirOfferKey = targetPlayer.PlayerUID + "," + invokingPlayer.PlayerUID;
      string ourOfferKey = invokingPlayer.PlayerUID + "," + targetPlayer.PlayerUID;
      // is there an active pact offer from the target player? begin the pact!
      if (activePactOffers.Contains(theirOfferKey)) {
        activePactOffers.Remove(ourOfferKey);
        activePactOffers.Remove(theirOfferKey);
        BeginPact(invokingPlayer, targetPlayer);
      }
      // is this an overlapping pact offer?
      else if (activePactOffers.Contains(ourOfferKey)) {
        SendChatMessage(invokingPlayer, "For arcane reasons, your pact offer cannot be renewed until it expires."); // TODO: allow refreshing pacts
      }
      else {
        activePactOffers.Add(ourOfferKey);
        // set pact offer to expire after some time
        sapi.Event.RegisterCallback((float dt) => {
          if (activePactOffers.Contains(ourOfferKey)) {
            activePactOffers.Remove(ourOfferKey);
            SendChatMessage(invokingPlayer, MESSAGE_PACT_OFFER_EXPIRED, targetPlayer.PlayerName);
          }
        }, OFFER_EXPIRE_TIME_MS);
      }
    }

    private Dictionary<string, string> activePactDeaths = new Dictionary<string, string>();

    private void BeginPact(IServerPlayer invokingPlayer, IServerPlayer targetPlayer) {
      activePactDeaths.Add(invokingPlayer.PlayerUID, targetPlayer.PlayerUID);
      activePactDeaths.Add(targetPlayer.PlayerUID, invokingPlayer.PlayerUID);
      KillPlayerForPact(invokingPlayer);
      KillPlayerForPact(targetPlayer);
    }

    private void KillPlayerForPact(IServerPlayer player) {
      SendChatMessage(player, MESSAGE_PACT_START);
      player.ClearSpawnPosition();
      player.Entity.Die(EnumDespawnReason.Death, new DamageSource() { Source = EnumDamageSource.Suicide, Type = EnumDamageType.Poison });
    }

    public void OnPlayerRevive(IServerPlayer revivingPlayer) {
      LogEvent("OnPlayerRevive: " + revivingPlayer.PlayerName);

      string otherPlayerUid;
      bool didFindPlayerUid = activePactDeaths.TryGetValue(revivingPlayer.PlayerUID, out otherPlayerUid);
      if (didFindPlayerUid) {

        // clean up activeDeathPacts
        activePactDeaths.Remove(revivingPlayer.PlayerUID);

        // reduce health to half
        float maxHealth = 9.0F + revivingPlayer.Entity.Stats.GetBlended("maxhealthExtraPoints"); // TODO: is there a better way to get this?
        float damage = maxHealth / 2F;
        revivingPlayer.Entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Suicide, Type = EnumDamageType.Poison }, damage);

        IServerPlayer otherPlayer = sapi.World.PlayerByUid(otherPlayerUid) as IServerPlayer;

        // the other player hasn't spawned yet?
        if (otherPlayer.Entity.Alive == false) {
          SendChatMessage(revivingPlayer, MESSAGE_PACT_STEP_FOR_ALIVE_PLAYER, otherPlayer.PlayerName);
          SendChatMessage(otherPlayer, MESSAGE_PACT_STEP_FOR_STILL_DEAD_PLAYER, revivingPlayer.PlayerName);
        }
        // the other player has spawned, teleport us! the pact is complete!
        else {
          revivingPlayer.Entity.TeleportTo(otherPlayer.Entity.Pos);
          sapi.BroadcastMessageToAllGroups(Lang.Get(MESSAGE_BROADCAST_PACT_COMPLETE, revivingPlayer.PlayerName, otherPlayer.PlayerName), EnumChatType.Notification);
        }
      }
    }

    private void SendChatMessage(IServerPlayer player, string message, params object[] param) {
      player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get(message, param), EnumChatType.Notification);
    }
  }
}