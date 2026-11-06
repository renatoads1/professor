let peerConnection;
let localStream;
let videoLocal;
let videoRemoto;
let dotNet;
let candidatosPendentes = [];

export async function iniciar(elementoLocal, elementoRemoto, referenciaDotNet, servidoresIce) {
    await encerrar();
    if (!navigator.mediaDevices?.getUserMedia || !window.RTCPeerConnection) {
        throw new Error("Este navegador não oferece suporte a chamadas WebRTC.");
    }

    videoLocal = elementoLocal;
    videoRemoto = elementoRemoto;
    dotNet = referenciaDotNet;
    localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
    videoLocal.srcObject = localStream;
    videoLocal.muted = true;
    videoLocal.autoplay = true;
    videoLocal.playsInline = true;
    videoRemoto.autoplay = true;
    videoRemoto.playsInline = true;

    peerConnection = new RTCPeerConnection({ iceServers: servidoresIce });
    for (const track of localStream.getTracks()) {
        peerConnection.addTrack(track, localStream);
    }

    peerConnection.onicecandidate = event => {
        if (!event.candidate || !dotNet) {
            return;
        }

        const candidato = event.candidate.toJSON();
        dotNet.invokeMethodAsync("EnviarCandidatoIceAsync", {
            candidato: candidato.candidate,
            sdpMid: candidato.sdpMid,
            sdpMLineIndex: candidato.sdpMLineIndex
        }).catch(() => {});
    };

    peerConnection.ontrack = event => {
        if (event.streams.length > 0) {
            videoRemoto.srcObject = event.streams[0];
        } else {
            const stream = videoRemoto.srcObject instanceof MediaStream
                ? videoRemoto.srcObject
                : new MediaStream();
            stream.addTrack(event.track);
            videoRemoto.srcObject = stream;
        }

        videoRemoto.play().catch(() => {});
    };

    peerConnection.onconnectionstatechange = () => {
        dotNet?.invokeMethodAsync("AtualizarEstadoConexaoAsync", peerConnection.connectionState).catch(() => {});
    };
}

export async function criarOferta() {
    assegurarConexao();
    const oferta = await peerConnection.createOffer();
    await peerConnection.setLocalDescription(oferta);
    return peerConnection.localDescription.sdp;
}

export async function criarResposta(sdpOferta) {
    assegurarConexao();
    await peerConnection.setRemoteDescription({ type: "offer", sdp: sdpOferta });
    await aplicarCandidatosPendentes();
    const resposta = await peerConnection.createAnswer();
    await peerConnection.setLocalDescription(resposta);
    return peerConnection.localDescription.sdp;
}

export async function definirResposta(sdpResposta) {
    assegurarConexao();
    await peerConnection.setRemoteDescription({ type: "answer", sdp: sdpResposta });
    await aplicarCandidatosPendentes();
}

export async function adicionarCandidato(candidato) {
    assegurarConexao();
    const candidatoIce = {
        candidate: candidato.candidato,
        sdpMid: candidato.sdpMid,
        sdpMLineIndex: candidato.sdpMLineIndex
    };
    if (!peerConnection.remoteDescription) {
        candidatosPendentes.push(candidatoIce);
        return;
    }

    await peerConnection.addIceCandidate(candidatoIce);
}

export function definirMicrofoneAtivo(ativo) {
    for (const track of localStream?.getAudioTracks() ?? []) {
        track.enabled = ativo;
    }
}

export function definirCameraAtiva(ativa) {
    for (const track of localStream?.getVideoTracks() ?? []) {
        track.enabled = ativa;
    }
}

export async function encerrar() {
    if (peerConnection) {
        peerConnection.onicecandidate = null;
        peerConnection.ontrack = null;
        peerConnection.onconnectionstatechange = null;
        peerConnection.close();
        peerConnection = null;
    }

    for (const track of localStream?.getTracks() ?? []) {
        track.stop();
    }

    if (videoLocal) {
        videoLocal.srcObject = null;
    }
    if (videoRemoto) {
        videoRemoto.srcObject = null;
    }

    localStream = null;
    videoLocal = null;
    videoRemoto = null;
    dotNet = null;
    candidatosPendentes = [];
}

function assegurarConexao() {
    if (!peerConnection) {
        throw new Error("A conexão de vídeo ainda não foi inicializada.");
    }
}

async function aplicarCandidatosPendentes() {
    const candidatos = candidatosPendentes;
    candidatosPendentes = [];
    for (const candidato of candidatos) {
        await peerConnection.addIceCandidate(candidato);
    }
}
