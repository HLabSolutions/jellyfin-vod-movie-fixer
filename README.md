# VOD Movie Fixer

<p align="center">
  <img src="images/logo.png" alt="VOD Movie Fixer logo" width="128" height="128">
</p>

Plugin per [Jellyfin](https://jellyfin.org) che corregge i film che il provider IPTV/VOD
espone come "serie" con una sola stagione e un solo episodio.

## Il problema

Con librerie basate su file `.strm`, alcuni provider VOD classificano i film come se
fossero serie TV: ogni film diventa una "serie" con Stagione 01 / Episodio 01 che contiene
l'intero film. Jellyfin li tratta quindi (locandina, pagina, ricerca) come serie TV invece
che come film.

## Cosa fa questo plugin

1. Nelle librerie che indichi in configurazione, cerca le "serie" con **esattamente 1
   stagione e 1 episodio**.
2. Per ogni candidato, interroga [TMDb](https://www.themoviedb.org/) confrontando la
   ricerca "film" e la ricerca "serie TV" per quel titolo: se il titolo corrisponde
   più a un film, il candidato è confermato.
3. Se confermato, sostituisce la voce Series/Season/Episode nel database di Jellyfin con
   una voce Movie che punta **allo stesso file `.strm`** — il file fisico non viene mai
   spostato, rinominato o toccato, viene modificata solo l'identificazione interna di
   Jellyfin. Subito dopo forza un refresh completo dei metadati del nuovo film (poster,
   trama, ecc.) tramite i provider film configurati sul server.
4. Per i candidati che TMDb non conferma automaticamente come film, la pagina di
   configurazione permette di **cercarli e assegnarli manualmente** uno per uno.

## Requisiti

- Jellyfin **12.0** o successivo (usa API introdotte in questa versione; non è
  compatibile con la serie 10.x).
- Una API key TMDb (v3 auth), gratuita: themoviedb.org → impostazioni account → API.

## Installazione

1. In Jellyfin: **Dashboard → Plugin → Repository → aggiungi repository**
   con questo URL:

   ```
   https://raw.githubusercontent.com/HLabSolutions/jellyfin-vod-movie-fixer/main/manifest.json
   ```

2. Vai su **Catalogo**, cerca **VOD Movie Fixer**, installa.
3. Riavvia il server.

In alternativa, per un'installazione manuale: scarica lo zip dell'
[ultima release](https://github.com/HLabSolutions/jellyfin-vod-movie-fixer/releases/latest)
ed estrailo in `plugins/VodMovieFixer_<versione>/` nella cartella dati di Jellyfin,
poi riavvia il server.

## Configurazione

Dashboard → Plugin → **VOD Movie Fixer**:

- **Librerie da analizzare**: nomi esatti delle librerie Jellyfin, separati da virgola.
  Se vuoto, il plugin non fa nulla (comportamento sicuro di default).
- **API key TMDb**: usata solo per confermare i candidati.
- **Modalità simulazione (dry run)**: attiva di default. Il plugin scrive nei log cosa
  convertirebbe senza modificare nulla. **Consigliato lasciarla attiva la prima volta**,
  controllare i log del server, e disattivarla solo dopo aver verificato che i candidati
  rilevati siano corretti.
- **Esegui automaticamente dopo ogni scansione libreria**: se attiva, la correzione
  parte da sola subito dopo che Jellyfin termina una scansione libreria.

In alternativa (o in aggiunta) puoi lanciare il task **"Correggi film VOD classificati
come serie"** manualmente da Dashboard → Programmazione attività → Libreria, oppure con
il pulsante **"Esegui ora"** direttamente nella pagina del plugin (che mostra anche un
riquadro con le righe di log del plugin, senza dover andare a recuperarle altrove).

### Assegnazione manuale

Nella sezione **"Assegnazione manuale"** della pagina di configurazione, il pulsante
**"Trova candidati da assegnare"** elenca le "serie" con 1 sola stagione/episodio per cui
TMDb non ha confermato automaticamente una corrispondenza come film (titoli ambigui, poco
popolari, o scritti in modo diverso dal titolo ufficiale). Per ciascuna puoi modificare il
testo di ricerca, cercare su TMDb e scegliere con un click il film corretto tra i
risultati: la conversione avviene subito, senza spostare il file `.strm`, e usa l'id TMDb
scelto (non una nuova ricerca automatica) per recuperare i metadati corretti.

## Limitazioni note

- L'euristica TMDb (popolarità + somiglianza titolo) può sbagliare su titoli ambigui o
  poco popolari: usa sempre prima la modalità simulazione.
- Una vera serie TV con una sola stagione e un solo episodio pubblicato verrebbe
  comunque interrogata su TMDb; viene convertita solo se TMDb la riconosce come film,
  non come serie.
- La conversione modifica il database di Jellyfin (elimina le voci Series/Season/Episode
  e crea una voce Movie); non è automaticamente reversibile con un click — per tornare
  indietro serve rimuovere il file dalla libreria e far ripartire una scansione completa.

## Build da sorgente

```
cd Jellyfin.Plugin.VodMovieFixer
dotnet publish -c Release -o ./artifact
```

Il pacchetto per un'installazione manuale contiene `Jellyfin.Plugin.VodMovieFixer.dll`
(da `./artifact`).
