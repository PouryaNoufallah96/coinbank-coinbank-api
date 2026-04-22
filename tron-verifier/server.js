const express = require("express");
const cors = require("cors");
const { TronWeb } = require("tronweb");

const app = express();

app.use(cors());
app.use(express.json());

const tronWeb = new TronWeb({
    fullHost: "https://api.trongrid.io"
});

app.post("/verify", async (req, res) => {
    try {
        const { message, signature, address } = req.body;

        if (!message || !signature || !address) {
            return res.status(400).json({
                valid: false,
                error: "Missing required fields"
            });
        }

        const recoveredAddress = await tronWeb.trx.verifyMessageV2(
            message,
            signature,
            address
        );

        console.log("Verification debug:", {
            message,
            signature,
            address,
            recoveredAddress
        });

        return res.json({
            valid: recoveredAddress === address,
            recoveredAddress
        });

    } catch (err) {
        console.error("Verify error:", err);

        return res.status(500).json({
            valid: false,
            error: "Signature verification failed",
            details: err.message
        });
    }
});

app.listen(3001, "0.0.0.0", () => {
    console.log("🔥 TRON verifier running on port 3001");
});
